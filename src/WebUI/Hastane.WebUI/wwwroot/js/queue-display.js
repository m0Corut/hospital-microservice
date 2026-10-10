// Bekleme salonu çağrı ekranı: SignalR ile canlı, 15 sn'de bir yedek yenileme.
// Güvenlik: sunucudan gelen her şey textContent ile yazılır (innerHTML YOK); hub anonim ve yalnız dinleme.
(function () {
    'use strict';

    var root = document.getElementById('queue-display');
    if (!root) return;

    var doctorId = root.dataset.doctorId;
    var pollUrl = root.dataset.pollUrl;
    var hubUrl = root.dataset.hubUrl;
    var POLL_MS = 15000;

    var nowBox = document.getElementById('now');
    var nowNumber = document.getElementById('now-number');
    var nowName = document.getElementById('now-name');
    var nextList = document.getElementById('next-list');
    var nextEmpty = document.getElementById('next-empty');
    var conn = document.getElementById('conn');
    var updated = document.getElementById('updated');

    var lastCalledKey = null;
    var refreshTimer = null;

    function setConn(ok, text) {
        conn.dataset.ok = ok ? 'true' : 'false';
        conn.textContent = text;
    }

    function render(data) {
        var current = data.current;
        if (current) {
            nowNumber.textContent = String(current.number);
            nowName.textContent = current.displayName;
            // Yeni çağrıda ekranı kısa süre vurgula.
            var key = current.number + '|' + current.calledAt;
            if (lastCalledKey !== null && key !== lastCalledKey) {
                nowBox.classList.remove('flash');
                void nowBox.offsetWidth;
                nowBox.classList.add('flash');
            }
            lastCalledKey = key;
        } else {
            nowNumber.textContent = '–';
            nowName.textContent = 'Henüz çağrılan yok';
            lastCalledKey = null;
        }

        while (nextList.firstChild) nextList.removeChild(nextList.firstChild);
        var next = data.next || [];
        next.forEach(function (item) {
            var li = document.createElement('li');
            var n = document.createElement('span');
            n.className = 'n';
            n.textContent = String(item.number);
            var name = document.createElement('span');
            name.textContent = item.displayName;
            li.appendChild(n);
            li.appendChild(name);
            nextList.appendChild(li);
        });
        nextEmpty.hidden = next.length > 0;
        updated.textContent = 'Son güncelleme ' + new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    }

    function refresh() {
        return fetch(pollUrl, { headers: { 'Accept': 'application/json' }, cache: 'no-store' })
            .then(function (r) {
                if (!r.ok) throw new Error('http ' + r.status);
                return r.json();
            })
            .then(function (data) {
                render(data);
                setConn(true, hubConnected ? 'Canlı' : 'Canlı (yedek yenileme)');
            })
            .catch(function () {
                setConn(false, 'Bağlantı yok, yeniden deneniyor…');
            });
    }

    // Olaylar arka arkaya gelirse tek yenileme.
    function scheduleRefresh() {
        if (refreshTimer) return;
        refreshTimer = setTimeout(function () {
            refreshTimer = null;
            refresh();
        }, 150);
    }

    // Yedek: SignalR kopsa da ekran en geç 15 sn içinde doğru olur.
    setInterval(refresh, POLL_MS);

    var hubConnected = false;

    if (window.signalR && hubUrl) {
        // Gateway WebSocket vekili: negotiate yok, yalnız WebSockets (ocelot.json: /gateway/hubs/queue).
        var connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl, { skipNegotiation: true, transport: signalR.HttpTransportType.WebSockets })
            // Varsayılan yeniden bağlanma 4 denemede vazgeçer; ekran asla vazgeçmesin (1 sn, 2 sn ... en çok 15 sn).
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: function (ctx) {
                    return Math.min(1000 * (ctx.previousRetryCount + 1), 15000);
                }
            })
            .configureLogging(signalR.LogLevel.None)
            .build();

        connection.on('QueueUpdated', function (payload) {
            // Yük yalnız {doctorId, number, displayName, status, calledAt}; başka doktorun olayını yok say.
            if (payload && payload.doctorId && payload.doctorId !== doctorId) return;
            scheduleRefresh();
        });

        // Yeniden bağlanınca gruplar sunucuda kaybolur: tekrar katıl ve durumu tazele.
        var join = function () {
            return connection.invoke('JoinDoctor', doctorId).then(function () {
                hubConnected = true;
                setConn(true, 'Canlı');
                return refresh();
            });
        };

        connection.onreconnecting(function () {
            hubConnected = false;
            setConn(false, 'Bağlantı koptu, yeniden bağlanılıyor…');
        });
        connection.onreconnected(function () { join().catch(function () { }); });

        // İlk bağlantı denemesi başarısız olursa (Gateway henüz kapalı) otomatik yeniden bağlanma devreye girmez: elle tekrar.
        var start = function () {
            connection.start().then(join).catch(function () {
                hubConnected = false;
                setTimeout(start, 5000);
            });
        };
        connection.onclose(function () {
            hubConnected = false;
            setConn(false, 'Bağlantı koptu, yeniden bağlanılıyor…');
            setTimeout(start, 5000);
        });
        start();
    }
})();
