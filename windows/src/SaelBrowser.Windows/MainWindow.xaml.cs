using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using SaelBrowser.Core.Analysis;
using SaelBrowser.Core.Articles;
using SaelBrowser.Core.Browser;
using SaelBrowser.Core.Evidence;
using SaelBrowser.Core.Facts;

namespace SaelBrowser.Windows;

public partial class MainWindow : Window
{
    private const string PrimaryBackend = "https://api.xn--ypay-99a.pl";
    private const string FallbackBackend = "https://api.alvsal.pl";
    private const string PrivacyUrl = "https://sael-andrzej.github.io/SaelBrowser/privacy.html";
    private readonly BrowserModeState _mode = new();
    private readonly AnalysisRequestGate _gate = new();
    private readonly AnalysisCoordinator _analysis;
    private readonly ArticleAnalysisCoordinator _articleAnalysis;
    private readonly SaelTitleRewriter _titleRewriter = new(new ClickbaitAnalyzer());
    private readonly IArticleExtractor _extractor = new ArticleExtractor();
    private CancellationTokenSource? _analysisCancellation;
    private FactResult? _latestResult;
    private ArticleAnalysisResult? _latestArticleResult;
    private readonly List<ClaimAnalysis> _progressiveResults = [];
    private Stopwatch _navigationClock = new();
    private TimeSpan? _firstVisibleElapsed;
    private TimeSpan? _neutralTitleElapsed;
    private TimeSpan? _firstVerdictElapsed;
    private TimeSpan? _domReadyElapsed;
    private TimeSpan? _provisionalExtractionElapsed;
    private TimeSpan _analysisStartedElapsed;
    private string? _navigationHtmlSnapshot;
    private string _saelScript = "";
    private bool _selfTesting;
    private readonly string? _diagnosticClaim;
    private readonly string? _diagnosticOutput;

    public MainWindow()
    {
        InitializeComponent();
        _diagnosticClaim = CommandLineValue("--diagnostic-claim=");
        _diagnosticOutput = CommandLineValue("--diagnostic-output=");
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SaelBrowser-Windows/0.1");
        var extractor = _extractor;
        var linkedHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(6) };
        var verifier = new PublicSourceVerifier(linkedHttp, extractor);
        var remote = new RemoteEvidenceProvider(http, verifier, PrimaryBackend, FallbackBackend);
        var linked = new LinkedSourceEvidenceProvider(verifier);
        var discoveryHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        discoveryHttp.DefaultRequestHeaders.UserAgent.ParseAdd("SaelBrowser-Windows/0.1 evidence-discovery");
        var institutional = new DiscoveryEvidenceProvider([new InstitutionalHistoryDiscovery()], verifier);
        var publicDiscovery = new DiscoveryEvidenceProvider([new GdeltSourceDiscovery(discoveryHttp), new WikimediaSourceDiscovery(discoveryHttp)], verifier);
        var factEngine = new FactEngine(new ClaimExtractor(), new ClickbaitAnalyzer(), new EvidenceEngine([remote, institutional, publicDiscovery, linked], TimeSpan.FromSeconds(8)));
        _analysis = new AnalysisCoordinator(extractor, factEngine);
        _articleAnalysis = new ArticleAnalysisCoordinator(extractor, new ClaimDecomposer(new ClaimExtractor()), factEngine, new AnalysisResultCache());
        Loaded += InitializeAsync;
        Closed += (_, _) => _analysisCancellation?.Cancel();
    }

    private async void InitializeAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            _saelScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Resources", "Sael", "sael.js"));
            var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaelBrowser", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
            await Browser.EnsureCoreWebView2Async(environment);
            Browser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            Browser.CoreWebView2.Settings.IsWebMessageEnabled = false;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = true;
            Browser.CoreWebView2.NavigationStarting += NavigationStarting;
            Browser.CoreWebView2.NavigationCompleted += NavigationCompleted;
            Browser.CoreWebView2.ContentLoading += ContentLoading;
            Browser.CoreWebView2.DOMContentLoaded += DomContentLoaded;
            Browser.CoreWebView2.HistoryChanged += (_, _) => UpdateNavigation();
            Browser.CoreWebView2.SourceChanged += (_, _) => AddressBar.Text = Browser.Source?.AbsoluteUri ?? "";
            Browser.CoreWebView2.NewWindowRequested += (_, args) => { args.Handled = true; Navigate(args.Uri); };
            Browser.CoreWebView2.ServerCertificateErrorDetected += (_, args) => args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            Browser.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            Browser.CoreWebView2.WebResourceRequested += BlockKnownAdvertisingRequest;
            UpdateMode();
            if (Environment.GetCommandLineArgs().Contains("--self-test", StringComparer.Ordinal))
            {
                _selfTesting = true;
                await RunSelfTestAsync();
            }
            else Navigate(_diagnosticClaim ?? "https://www.google.com");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            StatusText.Text = "Brak Microsoft Edge WebView2 Runtime. Uruchom ponownie instalator SaelBrowser.";
            MessageBox.Show("Do działania SaelBrowser jest wymagany Microsoft Edge WebView2 Runtime.", "Brak WebView2", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception error) { StatusText.Text = "Nie udało się uruchomić przeglądarki: " + error.Message; }
    }

    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _navigationClock = Stopwatch.StartNew();
        _firstVisibleElapsed = null;
        _neutralTitleElapsed = null;
        _firstVerdictElapsed = null;
        _domReadyElapsed = null;
        _provisionalExtractionElapsed = null;
        _gate.BeginNavigation();
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = new CancellationTokenSource();
        _latestResult = null;
        _latestArticleResult = null;
        _progressiveResults.Clear();
        _navigationHtmlSnapshot = null;
        VerdictButton.IsEnabled = false;
        VerdictButton.Content = "NIE WIEM • analizuję…";
        VerdictButton.Background = Brushes.Gold;
        StatusText.Text = "Ładowanie strony…";
        AddressBar.Text = e.Uri;
    }

    private async void ContentLoading(object? sender, CoreWebView2ContentLoadingEventArgs e)
    {
        if (_selfTesting || _mode.Mode != BrowserMode.Sael) return;
        try
        {
            await ApplySaelAsync("apply");
            _firstVisibleElapsed ??= _navigationClock.Elapsed;
        }
        catch { /* DOMContentLoaded remains the safe fallback. */ }
    }

    private async void DomContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
    {
        if (_selfTesting || _mode.Mode != BrowserMode.Sael) return;
        _domReadyElapsed = _navigationClock.Elapsed;
        await ShowImmediateProvisionalTitleAsync(_gate.Capture(Browser.Source?.AbsoluteUri ?? ""), _analysisCancellation?.Token ?? CancellationToken.None);
        var raw = await Browser.ExecuteScriptAsync(DomSnapshot.Script);
        _navigationHtmlSnapshot = JsonSerializer.Deserialize<string>(raw) ?? "";
        await ApplySaelAsync("apply");
        _firstVisibleElapsed ??= _navigationClock.Elapsed;
    }

    private async Task ShowImmediateProvisionalTitleAsync(AnalysisToken token, CancellationToken cancellationToken)
    {
        try
        {
            var extractionClock = Stopwatch.StartNew();
            const string script = "(() => ({ title: (document.querySelector('article h1,main h1,[role=main] h1,h1')?.innerText || document.title || '').trim(), lead: (document.querySelector('article p,main p,[role=main] p')?.innerText || '').trim() }))()";
            var raw = await Browser.ExecuteScriptAsync(script);
            var article = JsonSerializer.Deserialize<EarlyArticle>(raw);
            _provisionalExtractionElapsed = extractionClock.Elapsed;
            if (!_gate.IsCurrent(token, Browser.Source?.AbsoluteUri) || cancellationToken.IsCancellationRequested || _mode.Mode != BrowserMode.Sael) return;
            var provisional = _titleRewriter.Rewrite(article?.Title ?? "", article?.Lead ?? "", null, FactVerdict.Unknown);
            if (provisional.Length > 0 && await ResolvePrimaryTitleAsync(provisional, "SAEL analizuje…", false))
                _neutralTitleElapsed ??= _navigationClock.Elapsed;
        }
        catch (OperationCanceledException) { }
        catch { /* Full analysis remains authoritative. */ }
    }

    private async void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        UpdateNavigation();
        if (_selfTesting) return;
        if (!e.IsSuccess)
        {
            StatusText.Text = $"Błąd ładowania: {e.WebErrorStatus}";
            VerdictButton.Content = "NIE WIEM • błąd ładowania";
            return;
        }
        var completedRaw = await Browser.ExecuteScriptAsync(DomSnapshot.Script);
        var completedHtml = JsonSerializer.Deserialize<string>(completedRaw) ?? "";
        var originalHtml = (_navigationHtmlSnapshot?.Length ?? 0) >= completedHtml.Length ? _navigationHtmlSnapshot! : completedHtml;
        if (_mode.Mode == BrowserMode.Sael) await ApplySaelAsync("apply");
        else StatusText.Text = "ORYGINAŁ • strona bez zmian SAEL";
        await AnalyzeCurrentPageAsync(originalHtml);
    }

    private async Task AnalyzeCurrentPageAsync(string? originalHtml = null)
    {
        var url = Browser.Source?.AbsoluteUri ?? "";
        if (string.IsNullOrWhiteSpace(url) || _analysisCancellation is null) return;
        var token = _gate.Capture(url);
        var cancellation = _analysisCancellation.Token;
        try
        {
            var html = originalHtml;
            if (html is null)
            {
                var raw = await Browser.ExecuteScriptAsync(DomSnapshot.Script);
                html = JsonSerializer.Deserialize<string>(raw) ?? "";
            }
            if (!_gate.IsCurrent(token, Browser.Source?.AbsoluteUri) || cancellation.IsCancellationRequested) return;
            _analysisStartedElapsed = _navigationClock.Elapsed;
            var articleResult = await _articleAnalysis.AnalyzeAsync(html, url,
                (article, claims) => ClaimsReadyAsync(token, article, claims, cancellation),
                (article, result) => ClaimReadyAsync(token, url, article, result, cancellation), cancellation);
            if (!_gate.IsCurrent(token, Browser.Source?.AbsoluteUri) || cancellation.IsCancellationRequested) return;
            _latestArticleResult = articleResult;
            _latestResult = articleResult.Results.FirstOrDefault()?.Result;
            ShowArticleSummary(articleResult);
            var neutralTitle = ComposeNeutralTitle(articleResult.Article, articleResult.Results);
            var domTitleChanged = false;
            if (_mode.Mode == BrowserMode.Sael && neutralTitle.Length > 0)
                domTitleChanged = await ResolvePrimaryTitleAsync(neutralTitle, PrimaryBadge(articleResult.Results.FirstOrDefault()?.Result));
            var displayedDomTitle = await ReadDomTitleAsync();
            await WriteArticleTraceAsync(url, articleResult, neutralTitle, domTitleChanged, displayedDomTitle);
            if (_diagnosticClaim is not null) Close();
        }
        catch (OperationCanceledException) { }
        catch { if (_gate.IsCurrent(token, Browser.Source?.AbsoluteUri)) VerdictButton.Content = "NIE WIEM • analiza nie powiodła się"; }
    }

    private Task ClaimsReadyAsync(AnalysisToken token, ArticleInput article, IReadOnlyList<Claim> claims, CancellationToken cancellation)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            if (!_gate.IsCurrent(token, Browser.Source?.AbsoluteUri) || cancellation.IsCancellationRequested || _mode.Mode != BrowserMode.Sael) return;
            var provisional = _titleRewriter.Rewrite(article.Title, article.Content, claims.FirstOrDefault()?.Text, FactVerdict.Unknown);
            if (provisional.Length > 0 && await ResolvePrimaryTitleAsync(provisional, "SAEL analizuje…", false))
                _neutralTitleElapsed ??= _navigationClock.Elapsed;
        }).Task.Unwrap();
    }

    private Task ClaimReadyAsync(AnalysisToken token, string url, ArticleInput article, ClaimAnalysis result, CancellationToken cancellation)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            if (!_gate.IsCurrent(token, Browser.Source?.AbsoluteUri) || cancellation.IsCancellationRequested) return;
            _progressiveResults.RemoveAll(item => item.Index == result.Index);
            _progressiveResults.Add(result);
            _firstVerdictElapsed ??= _navigationClock.Elapsed;
            VerdictButton.Content = $"SAEL analizuje… {_progressiveResults.Count}/{result.TotalClaims}";
            VerdictButton.IsEnabled = true;
            if (result.Index == 0 && _mode.Mode == BrowserMode.Sael)
            {
                var neutral = ComposeNeutralTitle(article, _progressiveResults);
                if (neutral.Length > 0 && await ResolvePrimaryTitleAsync(neutral, PrimaryBadge(result.Result)))
                    _neutralTitleElapsed ??= _navigationClock.Elapsed;
            }
        }).Task.Unwrap();
    }

    private void ShowArticleSummary(ArticleAnalysisResult result)
    {
        if (result.Results.Count == 0)
            VerdictButton.Content = "NIE WIEM · brak konkretnych twierdzeń";
        else
            VerdictButton.Content = $"Sprawdzono {result.Results.Count} twierdzenia · {result.TrueCount} PRAWDA · {result.FalseCount} FAŁSZ · {result.UnknownCount} NIE WIEM";
        VerdictButton.Background = Brushes.DarkSlateGray;
        VerdictButton.Foreground = Brushes.White;
        VerdictButton.IsEnabled = true;
    }

    private string ComposeNeutralTitle(ArticleInput article, IReadOnlyList<ClaimAnalysis> results)
    {
        var verified = results.FirstOrDefault(item => item.Result.Verdict == FactVerdict.True);
        var primary = verified?.Claim.Text ?? results.OrderBy(item => item.Index).FirstOrDefault()?.Claim.Text;
        var verdict = verified?.Result.Verdict ?? FactVerdict.Unknown;
        return _titleRewriter.Rewrite(article.Title, article.Content, primary, verdict);
    }

    private static string PrimaryBadge(FactResult? result) => result?.Verdict switch
    {
        FactVerdict.True => $"PRAWDA {result.Confidence:P0}",
        FactVerdict.False => $"FAŁSZ {result.Confidence:P0}",
        _ => "NIE WIEM"
    };

    private async Task ApplySaelAsync(string action)
    {
        try
        {
            var raw = await Browser.ExecuteScriptAsync(_saelScript.Replace("__ACTION__", action, StringComparison.Ordinal));
            var json = JsonSerializer.Deserialize<string>(raw);
            var result = json is null ? null : JsonSerializer.Deserialize<SaelScriptResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            StatusText.Text = result?.Success == true
                ? action == "restore" ? "ORYGINAŁ • przywrócono stronę" : $"SAEL • ukryto {result.HiddenCount} elementów"
                : "SAEL nie mógł zmienić strony: " + (result?.Error ?? "brak odpowiedzi");
        }
        catch (Exception error) { StatusText.Text = "SAEL nie mógł zmienić strony: " + error.Message; }
    }

    private void ShowResult(FactResult result)
    {
        _latestResult = result;
        VerdictButton.Content = FactPresentation.VerdictButtonText(result);
        VerdictButton.Background = result.Verdict switch { FactVerdict.True => Brushes.LimeGreen, FactVerdict.False => Brushes.Firebrick, _ => Brushes.Gold };
        VerdictButton.Foreground = result.Verdict == FactVerdict.False ? Brushes.White : Brushes.Black;
        VerdictButton.IsEnabled = true;
    }

    private void BlockKnownAdvertisingRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;
        var host = uri.Host.ToLowerInvariant();
        if (host.EndsWith("doubleclick.net", StringComparison.Ordinal) || host.EndsWith("googlesyndication.com", StringComparison.Ordinal))
            e.Response = Browser.CoreWebView2.Environment.CreateWebResourceResponse(Stream.Null, 204, "No Content", "Content-Length: 0");
    }

    private void Navigate(string input)
    {
        var normalized = BrowserAddressNormalizer.Normalize(input);
        if (normalized is not null && Browser.CoreWebView2 is not null) Browser.CoreWebView2.Navigate(normalized);
    }
    private void UpdateNavigation()
    {
        BackButton.IsEnabled = Browser.CoreWebView2?.CanGoBack == true;
        ForwardButton.IsEnabled = Browser.CoreWebView2?.CanGoForward == true;
    }
    private void UpdateMode()
    {
        OriginalButton.Content = _mode.Mode == BrowserMode.Original ? "ORYGINAŁ ✓" : "ORYGINAŁ";
        SaelButton.Content = _mode.Mode == BrowserMode.Sael ? "SAEL ✓" : "SAEL";
    }

    private void Go_Click(object sender, RoutedEventArgs e) => Navigate(AddressBar.Text);
    private void AddressBar_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(AddressBar.Text); }
    private void Back_Click(object sender, RoutedEventArgs e) { if (Browser.CoreWebView2?.CanGoBack == true) Browser.CoreWebView2.GoBack(); }
    private void Forward_Click(object sender, RoutedEventArgs e) { if (Browser.CoreWebView2?.CanGoForward == true) Browser.CoreWebView2.GoForward(); }
    private void Reload_Click(object sender, RoutedEventArgs e) => Browser.CoreWebView2?.Reload();
    private async void Original_Click(object sender, RoutedEventArgs e)
    {
        if (!_mode.Select(BrowserMode.Original)) return;
        UpdateMode();
        await ApplySaelAsync("restore");
        VerdictButton.Content = "ORYGINAŁ · analiza SAEL ukryta";
    }
    private async void Sael_Click(object sender, RoutedEventArgs e)
    {
        if (!_mode.Select(BrowserMode.Sael)) return;
        UpdateMode();
        await ApplySaelAsync("apply");
        _firstVisibleElapsed ??= _navigationClock.Elapsed;
        if (_latestArticleResult is not null)
        {
            var neutral = ComposeNeutralTitle(_latestArticleResult.Article, _latestArticleResult.Results);
            await ResolvePrimaryTitleAsync(neutral, PrimaryBadge(_latestArticleResult.Results.FirstOrDefault()?.Result));
            ShowArticleSummary(_latestArticleResult);
        }
    }
    private void Privacy_Click(object sender, RoutedEventArgs e) => Navigate(PrivacyUrl);
    private void CloseDetails_Click(object sender, RoutedEventArgs e) => DetailsColumn.Width = new GridLength(0);
    private void Verdict_Click(object sender, RoutedEventArgs e)
    {
        if (_latestArticleResult is null && _latestResult is null) return;
        DetailsText.Text = _latestArticleResult is not null ? FormatDetails(_latestArticleResult) : FormatDetails(_latestResult!);
        DetailsColumn.Width = new GridLength(390);
    }
    private static string FormatDetails(ArticleAnalysisResult article)
    {
        var lines = new List<string>
        {
            $"Sprawdzono {article.Results.Count} twierdzenia · {article.TrueCount} PRAWDA · {article.FalseCount} FAŁSZ · {article.UnknownCount} NIE WIEM",
            $"Ekstrakcja: {article.ExtractionElapsed.TotalMilliseconds:F0} ms · claimy gotowe: {article.ClaimsReadyElapsed.TotalMilliseconds:F0} ms · całość: {article.TotalElapsed.TotalMilliseconds:F0} ms"
        };
        foreach (var item in article.Results.OrderBy(result => result.Index))
        {
            lines.AddRange(["", $"{item.Index + 1}. {item.Claim.Text}", $"Wynik: {FactPresentation.VerdictLabel(item.Result.Verdict)}" +
                (item.Result.Verdict == FactVerdict.Unknown ? " · brak wystarczających dowodów" : $" · {item.Result.Confidence:P0}"),
                $"Pierwsze evidence: {(item.FirstEvidenceElapsed is null ? "brak" : $"{item.FirstEvidenceElapsed.Value.TotalMilliseconds:F0} ms")} · verdict: {item.VerdictElapsed.TotalMilliseconds:F0} ms" + (item.FromCache ? " · CACHE" : ""),
                $"Confidence techniczne: {item.Result.Confidence:P0}"]);
            foreach (var set in item.Result.EvidenceSets)
            {
                foreach (var cluster in set.Clusters ?? [])
                    lines.Add($"• {cluster.Id}: {cluster.Confidence:P0} — {string.Join(", ", cluster.Items.Select(evidence => evidence.Domain + "/" + evidence.Stance))}");
                foreach (var evidence in set.Items) lines.Add($"• {evidence.Publisher}: {evidence.Summary}\n  {evidence.Url}");
            }
        }
        return string.Join(Environment.NewLine, lines);
    }
    private static string FormatDetails(FactResult result)
    {
        var lines = new List<string> { $"WERDYKT: {FactPresentation.VerdictLabel(result.Verdict)}", $"Pewność: {result.Confidence:P0}", "", result.Rationale, "", $"Clickbait: {result.Clickbait.Score:P0}", string.Join("; ", result.Clickbait.Reasons), "", "TWIERDZENIA I DOWODY" };
        foreach (var set in result.EvidenceSets)
        {
            lines.AddRange(["", set.Claim.Text, set.Message]);
            lines.Add($"Confidence przed progiem: potwierdza {set.SupportConfidence:P0}, obala {set.RefuteConfidence:P0}");
            lines.Add($"Po progu bezpieczeństwa: {(set.Sufficient ? "wystarczające" : "niewystarczające")}");
            foreach (var cluster in set.Clusters ?? [])
                lines.Add($"• {cluster.Id}: {cluster.Confidence:P0} — {string.Join(", ", cluster.Items.Select(item => item.Domain + "/" + item.Stance))}");
            foreach (var item in set.Items) lines.Add($"• {item.Publisher}: {item.Summary}\n  {item.Url}");
            foreach (var error in set.ProviderErrors) lines.Add("• provider: " + error);
        }
        if (result.EvidenceSets.Count == 0) lines.Add("Brak konkretnego twierdzenia do oceny.");
        return string.Join(Environment.NewLine, lines);
    }

    private async Task RunSelfTestAsync()
    {
        var checks = new Dictionary<string, bool>();
        try
        {
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Complete(object? sender, CoreWebView2NavigationCompletedEventArgs args) => loaded.TrySetResult(args.IsSuccess);
            Browser.CoreWebView2.NavigationCompleted += Complete;
            Browser.NavigateToString("""
              <!doctype html><html><head><title>Raport testowy</title></head><body>
              <nav>menu</nav><article><h1><a href="https://example.invalid/report">SZOK! Nie uwierzysz, co stało się w Warszawie</a></h1>
              <p>Rada Warszawy przyjęła budżet na 2026 rok. Za uchwałą głosowało 32 radnych.</p>
              <div class="advertisement">reklama</div>
              <div id="login" role="dialog" style="position:fixed">Zaloguj się do konta</div></article></body></html>
              """);
            checks["document-load"] = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Browser.CoreWebView2.NavigationCompleted -= Complete;
            await ApplySaelAsync("apply");
            checks["ad-hidden"] = await ScriptBoolAsync("document.querySelector('.advertisement').classList.contains('sael-hidden')");
            checks["login-preserved"] = !await ScriptBoolAsync("document.querySelector('#login').classList.contains('sael-hidden')");
            checks["progressive-blur"] = await ScriptBoolAsync("document.querySelector('h1').classList.contains('sael-analyzing') && !!document.querySelector('h1 > .sael-analysis-text')");
            checks["progress-label"] = await ScriptBoolAsync("document.querySelector('h1 > .sael-progress')?.innerText.includes('analizuje')");
            var neutral = _titleRewriter.Rewrite("SZOK! Nie uwierzysz, co stało się w Warszawie", "Rada Warszawy przyjęła budżet na 2026 rok. Za uchwałą głosowało 32 radnych.");
            checks["progressive-resolve"] = await ResolvePrimaryTitleAsync(neutral, "NIE WIEM");
            checks["clickbait-title-neutralized"] = await ScriptBoolAsync("document.querySelector('h1').innerText.includes('Rada Warszawy przyjęła budżet')");
            checks["title-link-preserved"] = await ScriptBoolAsync("document.querySelector('h1 a')?.getAttribute('href') === 'https://example.invalid/report'");
            await Browser.ExecuteScriptAsync("(() => { const e=document.createElement('div'); e.id='dynamic-newsletter'; e.className='newsletter'; e.style.position='fixed'; e.textContent='Subscribe newsletter'; document.body.appendChild(e); })()");
            await Browser.ExecuteScriptAsync("(() => { const e=document.createElement('div'); e.id='onet-premium-overlay'; e.className='premium-overlay'; e.style.cssText='position:fixed;inset:0'; e.textContent='Oferta premium — wyłącz reklamy i kup dostęp'; document.body.appendChild(e); })()");
            await Task.Delay(500);
            checks["dynamic-hidden"] = await ScriptBoolAsync("document.querySelector('#dynamic-newsletter').classList.contains('sael-hidden')");
            checks["dynamic-premium-hidden"] = await ScriptBoolAsync("document.querySelector('#onet-premium-overlay').classList.contains('sael-hidden')");
            await ApplySaelAsync("restore");
            checks["restore"] = !await ScriptBoolAsync("document.querySelector('.advertisement').classList.contains('sael-hidden')");
            checks["original-title-restored"] = await ScriptBoolAsync("document.querySelector('h1').innerText.includes('SZOK! Nie uwierzysz') && !!document.querySelector('h1 a')");
            var raw = await Browser.ExecuteScriptAsync("document.querySelector('article').outerHTML");
            var html = JsonSerializer.Deserialize<string>(raw) ?? "";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await _analysis.AnalyzeAsync(html, "https://self-test.invalid/article", timeout.Token);
            checks["analysis-unknown-without-independent-proof"] = result.Verdict == FactVerdict.Unknown;
        }
        catch { checks["unhandled-error"] = false; }

        var output = Environment.GetEnvironmentVariable("SAEL_SELF_TEST_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        }
        Environment.Exit(checks.Count >= 13 && checks.Values.All(value => value) ? 0 : 2);
    }

    private async Task<bool> ScriptBoolAsync(string expression) =>
        JsonSerializer.Deserialize<bool>(await Browser.ExecuteScriptAsync(expression));

    private async Task<bool> ResolvePrimaryTitleAsync(string title, string badgeText, bool completed = true)
    {
        var encoded = JsonSerializer.Serialize(title);
        var encodedBadge = JsonSerializer.Serialize(badgeText);
        var completedJson = completed ? "true" : "false";
        var script = $$"""
          (() => {
            const title = {{encoded}};
            const badgeText = {{encodedBadge}};
            const completed = {{completedJson}};
            const el = document.querySelector('article h1,main h1,[role="main"] h1,h1');
            const state = window.__saelState;
            if (!el || !state || !title) return false;
            if (!state.titles.has(el)) state.titles.set(el, { originalHtml: el.innerHTML });
            if (!el.hasAttribute('data-sael-original-title')) el.setAttribute('data-sael-original-title', (el.innerText || '').trim());
            let text = el.querySelector(':scope > .sael-analysis-text');
            if (!text) {
              text = document.createElement('span'); text.className = 'sael-analysis-text';
              while (el.firstChild) text.appendChild(el.firstChild);
              el.appendChild(text);
            }
            const link = text.matches('a[href]') ? text : text.querySelector('a[href]');
            if (link) link.textContent = title; else text.textContent = title;
            let badge = el.querySelector(':scope > .sael-progress');
            if (!badge) { badge = document.createElement('span'); badge.className = 'sael-progress'; el.appendChild(badge); }
            badge.textContent = badgeText;
            el.classList.add('sael-title');
            if (completed) {
              el.classList.remove('sael-analyzing'); el.classList.add('sael-resolved'); el.removeAttribute('data-sael-analyzing');
            } else {
              el.classList.remove('sael-analyzing'); el.classList.add('sael-resolved'); el.setAttribute('data-sael-analyzing', 'true');
            }
            return (text.innerText || '').trim() === title;
          })()
          """;
        return JsonSerializer.Deserialize<bool>(await Browser.ExecuteScriptAsync(script));
    }

    private async Task<string> ReadDomTitleAsync()
    {
        var raw = await Browser.ExecuteScriptAsync("(document.querySelector('article h1,main h1,[role=\"main\"] h1,h1')?.querySelector(':scope > .sael-analysis-text')?.innerText || document.querySelector('article h1,main h1,[role=\"main\"] h1,h1')?.innerText || '').trim()");
        return JsonSerializer.Deserialize<string>(raw) ?? "";
    }

    private async Task WriteArticleTraceAsync(string url, ArticleAnalysisResult articleResult, string? neutralTitle, bool domTitleChanged, string displayedDomTitle)
    {
        try
        {
            var trace = new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                inputText = _diagnosticClaim ?? SearchQueryFromUrl(url) ?? AddressBar.Text,
                googleSearchUrl = url,
                extractedArticleTitle = articleResult.Article.Title,
                extractedContentLength = articleResult.Article.Content.Length,
                extractedContentPreview = articleResult.Article.Content[..Math.Min(4000, articleResult.Article.Content.Length)],
                claimCandidates = articleResult.Claims.Select(claim => new { claim.Text, claim.Priority, claim.IsFactual }),
                claims = articleResult.Results.OrderBy(item => item.Index).Select(item => new
                {
                    claim = item.Claim.Text,
                    evidenceQueries = EvidenceSemantics.QueryVariants(item.Claim.Text),
                    evidence = item.Result.EvidenceSets.SelectMany(set => set.Items).Select(evidence => new { evidence.Publisher, evidence.Url, evidence.Domain, evidence.PrimarySourceId, stance = evidence.Stance.ToString().ToUpperInvariant(), evidence.Confidence, evidence.SourceType }),
                    clusters = item.Result.EvidenceSets.SelectMany(set => set.Clusters ?? []).Select(cluster => new { cluster.Id, cluster.Confidence, items = cluster.Items.Select(evidence => new { evidence.Domain, evidence.PrimarySourceId, stance = evidence.Stance.ToString().ToUpperInvariant() }) }),
                    verdict = item.Result.Verdict.ToString().ToUpperInvariant(),
                    item.Result.Confidence,
                    item.Result.Rationale,
                    item.FromCache,
                    firstEvidenceMs = item.FirstEvidenceElapsed?.TotalMilliseconds,
                    verdictMs = item.VerdictElapsed.TotalMilliseconds,
                    diagnostics = item.Result.EvidenceSets.SelectMany(set => set.Diagnostics ?? []).Select(diagnostic => new { diagnostic.Provider, diagnostic.Query, diagnostic.CandidateUrl, diagnostic.Stage, diagnostic.Accepted, diagnostic.Reason, elapsedMs = diagnostic.Elapsed.TotalMilliseconds }),
                    stageTimings = item.Result.EvidenceSets.Select(set => new { discoveryMs = set.StageTimings?.Discovery.TotalMilliseconds, fetchMs = set.StageTimings?.Fetch.TotalMilliseconds, classificationMs = set.StageTimings?.Classification.TotalMilliseconds, clusteringMs = set.StageTimings?.Clustering.TotalMilliseconds, factEngineMs = Math.Max(0, (item.VerdictElapsed - (set.TotalElapsed ?? TimeSpan.Zero)).TotalMilliseconds) })
                }),
                summary = new { checkedClaims = articleResult.Results.Count, articleResult.TrueCount, articleResult.FalseCount, articleResult.UnknownCount },
                timings = new { domReadyMs = _domReadyElapsed?.TotalMilliseconds, domReadyToExtractionMs = _domReadyElapsed is null || _provisionalExtractionElapsed is null ? null : _provisionalExtractionElapsed?.TotalMilliseconds, extractionToProvisionalTitleMs = _neutralTitleElapsed is null || _domReadyElapsed is null || _provisionalExtractionElapsed is null ? null : (_neutralTitleElapsed - _domReadyElapsed - _provisionalExtractionElapsed)?.TotalMilliseconds, navigationToExtractionMs = (_analysisStartedElapsed + articleResult.ExtractionElapsed).TotalMilliseconds, claimGenerationMs = (articleResult.ClaimsReadyElapsed - articleResult.ExtractionElapsed).TotalMilliseconds, navigationToFirstVisibleSaelMs = _firstVisibleElapsed?.TotalMilliseconds, navigationToNeutralTitleMs = _neutralTitleElapsed?.TotalMilliseconds, navigationToFirstVerdictMs = _firstVerdictElapsed?.TotalMilliseconds, navigationToCompleteMs = _navigationClock.Elapsed.TotalMilliseconds },
                neutralTitle,
                domTitleChanged,
                displayedDomTitle,
                displayed = VerdictButton.Content?.ToString()
            };
            var output = _diagnosticOutput;
            if (string.IsNullOrWhiteSpace(output))
                output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaelBrowser", "analysis-trace.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(trace, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Diagnostics must never alter the analysis result or UI. */ }
    }

    private static string? CommandLineValue(string prefix) => Environment.GetCommandLineArgs()
        .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static string? SearchQueryFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !(uri.Host.Equals("google.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase))) return null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (!Uri.UnescapeDataString(parts[0]).Equals("q", StringComparison.OrdinalIgnoreCase)) continue;
            return Uri.UnescapeDataString((parts.Length == 2 ? parts[1] : "").Replace('+', ' '));
        }
        return null;
    }

    private sealed record SaelScriptResult(bool Success, int HiddenCount, bool Observing, string? Error);
    private sealed record EarlyArticle(string Title, string Lead);
}
