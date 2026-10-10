// Hasta ve doktor sıra sayfaları: kuyrukta değişiklik olunca sayfayı yeniler (isteğe bağlı canlılık).
// Hub anonim ve yalnız dinleme; yalnız doktor kimliği (gizli değil) ile gruba katılınır. Kişisel veri yok.
(function () {
    'use strict';

    var el = document.querySelector('[data-queue-live]');
    if (!el || !window.signalR) return;

    var hubUrl = el.dataset.hubUrl;
    var doctorIds = (el.dataset.doctorIds || '').split(',').filter(function (s) { return s.length > 0; });
    if (!hubUrl || doctorIds.length === 0) return;

    var reloadTimer = null;
    var connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, { skipNegotiation: true, transport: signalR.HttpTransportType.WebSockets })
        .withAutomaticReconnect({ nextRetryDelayInMilliseconds: function (ctx) { return Math.min(1000 * (ctx.previousRetryCount + 1), 15000); } })
        .configureLogging(signalR.LogLevel.None)
        .build();

    connection.on('QueueUpdated', function () {
        if (reloadTimer) return;
        // Kısa gecikme: aynı anda gelen olaylar tek yenilemeyle toplanır.
        reloadTimer = setTimeout(function () { window.location.reload(); }, 700);
    });

    function join() {
        return Promise.all(doctorIds.map(function (id) { return connection.invoke('JoinDoctor', id); }));
    }

    connection.onreconnected(function () { join().catch(function () { }); });
    connection.start().then(join).catch(function () { /* canlı yenileme isteğe bağlı: sayfa yine çalışır */ });
})();
