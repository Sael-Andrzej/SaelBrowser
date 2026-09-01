const CACHE='sael-v2';
const FILES=['./','./index.html','./privacy.html','./manifest.webmanifest','./assets/sael-analysis.png','./assets/sael-details.png'];
self.addEventListener('install',e=>e.waitUntil(caches.open(CACHE).then(c=>c.addAll(FILES))));
self.addEventListener('fetch',e=>e.respondWith(fetch(e.request).catch(()=>caches.match(e.request))));
