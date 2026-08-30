# Bezpieczny deployer SAEL — projekt

Skrypty nie zostały uruchomione na produkcji. Nie odwołują się do `/var/www/zypay`, WordPressa, PM2 ani Race1v1.

Docelowo należy utworzyć osobnego użytkownika `sael-deployer` bez shella oraz root-owned dispatcher akceptujący wyłącznie `deploy`, `status`, `logs`, `rollback`. W `sudoers` należy dopuścić dokładnie te root-owned skrypty z ustalonymi argumentami — bez edytorów, powłoki, ogólnego `docker`, `docker compose` i dostępu do Docker socket. Alternatywnie klucz SSH może mieć `command="/usr/local/sbin/sael-dispatcher"`, `no-port-forwarding`, `no-agent-forwarding`, `no-X11-forwarding`, `no-pty`.

Sekrety powinny należeć do `root:root` z trybem `0600` w `/etc/sael/backend.env`. Agent nie powinien ich odczytywać; uprzywilejowany skrypt przekazuje je wyłącznie kontenerowi. Konfiguracja Nginx wymaga backupu pojedynczego zmienianego pliku, `nginx -t` i dopiero potem `reload`.
