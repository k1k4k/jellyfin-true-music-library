/*
 * Jellyfin True Music Library : un seul onglet « Artistes » dans les bibliothèques musique de l'interface web.
 * Injecté dans index.html par le plugin, une fois la médiathèque réparée (le serveur sert déjà « Artistes » comme
 * « Artistes de l'album » : les deux onglets montreraient la même liste).
 * - cache l'onglet « Artistes de l'album » (onglet 2 des bibliothèques musique), dans l'interface classique comme
 *   dans la nouvelle ;
 * - si cet onglet est ouvert (lien, onglet d'accueil mémorisé), bascule sur « Artistes ».
 * Ne touche à rien d'autre ; sans effet hors des bibliothèques musique.
 */
(function () {
    'use strict';

    var ALBUM_ARTISTS = 2;
    var ARTISTS = 3;
    var scheduled = false;

    function route() {
        var hash = (window.location.hash || '').replace(/^#!?/, '');
        var parts = hash.split('?');
        return { path: parts[0] || '', query: parts[1] || '' };
    }

    function isMusicLibrary() {
        return /(^|\/)music(\.html)?$/i.test(route().path);
    }

    function tabParam() {
        var match = /(?:^|&)tab=(\d+)(?:&|$)/.exec(route().query);
        return match ? parseInt(match[1], 10) : null;
    }

    function hide(element) {
        if (element && element.style.display !== 'none') {
            element.style.display = 'none';
            element.setAttribute('aria-hidden', 'true');
        }
    }

    function toArtistsUrl() {
        var r = route();
        var query = /(?:^|&)tab=\d+/.test(r.query)
            ? r.query.replace(/((?:^|&)tab=)\d+/, '$1' + ARTISTS)
            : (r.query ? r.query + '&' : '') + 'tab=' + ARTISTS;
        return '#' + r.path + '?' + query;
    }

    function apply() {
        scheduled = false;
        if (!isMusicLibrary()) {
            return;
        }

        // Lien direct vers l'onglet « Artistes de l'album » : même page, onglet « Artistes ».
        if (tabParam() === ALBUM_ARTISTS) {
            window.location.replace(toArtistsUrl());
            return;
        }

        // Interface classique (et TV) : boutons d'onglet de l'en-tête.
        var legacy = document.querySelector('.emby-tab-button[data-index="' + ALBUM_ARTISTS + '"]');
        if (legacy) {
            var active = legacy.classList.contains('emby-tab-button-active');
            hide(legacy);
            if (active) {
                var artists = document.querySelector('.emby-tab-button[data-index="' + ARTISTS + '"]');
                if (artists) {
                    artists.click();
                }
            }
        }

        // Nouvelle interface : menu des vues de la bibliothèque (mêmes positions que les onglets).
        var menu = document.getElementById('library-view-menu');
        if (menu) {
            var items = menu.querySelectorAll('[role="menuitem"]');
            if (items.length > ARTISTS) {
                var selected = items[ALBUM_ARTISTS].classList.contains('Mui-selected');
                hide(items[ALBUM_ARTISTS]);
                if (selected) {
                    items[ARTISTS].click();
                }
            }
        }
    }

    function schedule() {
        if (!scheduled) {
            scheduled = true;
            // Minuteur plutôt que requestAnimationFrame : il tourne aussi quand la page n'est pas dessinée (onglet en fond).
            window.setTimeout(apply, 50);
        }
    }

    window.addEventListener('hashchange', schedule);
    window.addEventListener('popstate', schedule);
    new MutationObserver(schedule).observe(document.documentElement, { childList: true, subtree: true });
    schedule();
})();
