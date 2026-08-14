// Light/dark theme handling. This file is loaded synchronously in <head> (before body
// paints) so window.rrsmTheme.init() can apply the right theme before any content is
// visible, avoiding a flash of the wrong theme. Blazor components talk to the same
// functions via JS interop (see Services/ThemeService.cs) to read/change the theme
// after the app has loaded.
window.rrsmTheme = {
    STORAGE_KEY: 'rrsm-theme',

    // Resolves to "light" or "dark": the user's stored explicit choice if one exists,
    // otherwise the OS/browser preference.
    resolve: function () {
        var stored = null;
        try {
            stored = localStorage.getItem(window.rrsmTheme.STORAGE_KEY);
        } catch (e) { /* localStorage unavailable (e.g. private browsing) - ignore */ }

        if (stored === 'light' || stored === 'dark') {
            return stored;
        }

        return (window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches)
            ? 'light'
            : 'dark';
    },

    apply: function (theme) {
        document.documentElement.setAttribute('data-theme', theme);
    },

    get: function () {
        return window.rrsmTheme.resolve();
    },

    set: function (theme) {
        try {
            localStorage.setItem(window.rrsmTheme.STORAGE_KEY, theme);
        } catch (e) { /* ignore */ }
        window.rrsmTheme.apply(theme);
    },

    init: function () {
        window.rrsmTheme.apply(window.rrsmTheme.resolve());
    }
};

window.rrsmTheme.init();
