// Subgen plugin: adds a "Generate subtitles" button on item pages
// (series, season, episode, movie) for administrators and users with "Allow this user to edit subtitles".
(function () {
    'use strict';

    var BUTTON_CLASS = 'subgenGenerateButton';
    var TYPES = ['Series', 'Season', 'Episode', 'Movie'];
    var busy = false;

    function itemIdFromUrl() {
        var m = /[?&]id=([0-9a-fA-F-]{32,36})/.exec(window.location.hash || window.location.search);
        return m ? m[1] : null;
    }

    // Small message at the bottom of the screen that goes away by itself (no pop-up).
    var toastEl = null;
    var toastTimer = null;
    function toast(text) {
        if (!toastEl) {
            toastEl = document.createElement('div');
            toastEl.style.cssText = 'position:fixed;left:50%;bottom:32px;transform:translateX(-50%);' +
                'background:rgba(32,32,32,.95);color:#fff;padding:12px 20px;border-radius:6px;' +
                'font-size:15px;z-index:99999;box-shadow:0 4px 16px rgba(0,0,0,.5);max-width:90vw;' +
                'transition:opacity .3s;pointer-events:none';
            document.body.appendChild(toastEl);
        }
        toastEl.textContent = text;
        toastEl.style.opacity = '1';
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { toastEl.style.opacity = '0'; }, 5000);
    }

    function label(type) {
        if (type === 'Season') return 'Generate subtitles for season';
        if (type === 'Series') return 'Generate subtitles for series';
        return 'Generate subtitles';
    }

    function findButtonRow() {
        // Visible row with the play / shuffle buttons on the item page.
        var rows = document.querySelectorAll('.mainDetailButtons, .detailButtons');
        for (var i = 0; i < rows.length; i++) {
            if (rows[i].offsetParent !== null) return rows[i];
        }
        return null;
    }

    function addButton(row, item) {
        var old = row.querySelector('.' + BUTTON_CLASS);
        if (old) {
            if (old.getAttribute('data-id') === item.Id) return;
            old.remove();
        }

        var btn = document.createElement('button');
        btn.type = 'button';
        // Do not use Jellyfin's own button classes (like btnPlaystate): Jellyfin binds those.
        btn.className = 'button-flat detailButton emby-button ' + BUTTON_CLASS;
        btn.setAttribute('data-id', item.Id);
        btn.title = label(item.Type);
        btn.innerHTML = '<div class="detailButton-content"><span class="material-icons detailButton-icon closed_caption" aria-hidden="true"></span></div>';

        btn.addEventListener('click', function () {
            if (busy) return;
            busy = true;
            toast('Subgen: sending…');
            ApiClient.fetch({
                url: ApiClient.getUrl('Subgen/Generate/' + item.Id),
                type: 'POST',
                dataType: 'json'
            }).then(function (r) {
                var msg;
                var embedded = r.embedded || 0;
                if (r.total === 0) {
                    msg = 'Subgen: found no episodes or movies under "' + item.Name + '".';
                } else if (r.sent === 0 && r.total === 1) {
                    msg = embedded
                        ? 'Subgen: this already has English subtitles inside the file. Use Edit subtitles → Search to make one anyway.'
                        : 'Subgen: this already has a subtitle. Delete it in Edit subtitles to make a new one.';
                } else if (r.sent === 0) {
                    msg = 'Subgen: all ' + r.total + ' already have subtitles.';
                } else {
                    msg = 'Subgen: making ' + r.sent + ' subtitle(s). They show up here when ready.';
                }
                if (r.total > 1 && (r.skipped > 0 || embedded > 0)) {
                    var parts = [];
                    if (r.skipped > 0) parts.push(r.skipped + ' had a subtitle file');
                    if (embedded > 0) parts.push(embedded + ' have English subtitles inside the file');
                    msg += ' Skipped: ' + parts.join(', ') + '.';
                }
                toast(msg);
            }, function () {
                toast('Subgen: request failed. Is the Subgen server running?');
            }).then(function () { busy = false; });
        });

        // Put it just before the ⋮ (more) button, so it sits after the heart.
        var more = row.querySelector('.btnMoreCommands');
        if (more) {
            row.insertBefore(btn, more);
        } else {
            row.appendChild(btn);
        }
    }

    var lastId = null;
    var lastItem = null;

    function check() {
        if (!window.ApiClient) return;
        var id = itemIdFromUrl();
        if (!id) return;
        var row = findButtonRow();
        if (!row) return;

        if (lastItem && lastId === id) {
            addButton(row, lastItem);
            return;
        }
        if (lastId === id) return; // still loading
        lastId = id;
        lastItem = null;

        ApiClient.getCurrentUser().then(function (user) {
            // Same rule as Jellyfin's "Edit subtitles": admins, or users with "Allow this user to edit subtitles".
            if (!user || !user.Policy || !(user.Policy.IsAdministrator || user.Policy.EnableSubtitleManagement)) return null;
            return ApiClient.getItem(user.Id, id);
        }).then(function (item) {
            if (!item || TYPES.indexOf(item.Type) < 0 || itemIdFromUrl() !== id) return;
            lastItem = item;
            var r = findButtonRow();
            if (r) addButton(r, item);
        }, function () { lastId = null; });
    }

    new MutationObserver(function () { check(); })
        .observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', check);
    check();
})();
