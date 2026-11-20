/**
 * Shared currency-formatting helper for dashboard KPI widgets.
 *
 * VIS.Util.formatCompactAmount(value, isoCode, precision)
 *   Formats the *magnitude* of a currency amount into a compact, locale-aware
 *   string. The numbering system depends on the base (accounting-schema) currency:
 *
 *     • Indian-numbering currencies (INR, PKR, BDT, NPR, BTN, LKR)
 *            → Indian system:        Thousand(K), Lakh(L), Crore(Cr), Arab(Ar)
 *                                    e.g. 25,000 -> 25K, 2,50,000 -> 2.5L,
 *                                         5,00,00,000 -> 5Cr, 1,00,00,00,000 -> 1Ar
 *     • else → International system: Thousand(K), Million(M), Billion(B), Trillion(T)
 *                                    e.g. 25,000 -> 25K, 2,500,000 -> 2.5M,
 *                                         5,000,000,000 -> 5B, 1,000,000,000,000 -> 1T
 *
 *   The compact fraction (and any sub-thousand amount) is kept to the base
 *   currency's precision; parseFloat then drops trailing zeros. The returned
 *   string is always non-negative — callers prepend their own sign and currency
 *   symbol so each widget keeps its own composition.
 *
 * VAS.formatAmountByBrowser(value, isoCode, precision, { compact, symbol })
 *   Format an amount in the browser's culture (window.navigator.language) with the
 *   currency the SERVER sent: ISO code + precision; the server's symbol only as a
 *   fallback when the browser has no sign for the ISO code. Separators, decimals and
 *   where the currency sign and the minus go come from the browser culture
 *   (en-US "-$1,234.50", de-DE "-1.234,50 €", sl-SI "-1.234,50 €", en-IN "₹1,234.50").
 *   compact: true -> the digits from formatCompactAmount above (K / M / B, or L / Cr
 *   for Indian-numbering currencies) with the currency sign placed the same way.
 *   Precision: the given one, else the context's StdPrecision, else 2 (max 10).
 *   Optional, so a widget keeps its own look while the culture places the parts:
 *     symbolWrap(signText) -> HTML for the currency sign, e.g. a styled span
 *                             (the caller escapes signText itself);
 *     minusSign            -> the minus to use (default "-", e.g. "−");
 *     positiveSign         -> a sign for amounts above zero (default none, e.g. "+").
 *   Dashboard widgets show compact amounts through this function (with compact: true)
 *   instead of composing sign + symbol + VIS.Util.formatCompactAmount themselves.
 */
; VIS = window.VIS || {};

; (function (VIS) {

    VIS.Util = VIS.Util || {};

    /* Compact-scale tiers, largest divisor first. */
    var INDIAN_TIERS = [
        /*[1e9, 'Ar'],*/  // 1,00,00,00,000 -> 1Ar
        [1e7, 'Cr'],  //    5,00,00,000 -> 5Cr
        [1e5, 'L'],   //      2,50,000   -> 2.5L
        [1e3, 'K']    //        25,000   -> 25K
    ];
    var INTL_TIERS = [
        /*[1e12, 'T'],*/  // 1,000,000,000,000 -> 1T
        [1e9, 'B'],   //     5,000,000,000 -> 5B
        [1e6, 'M'],   //         2,500,000 -> 2.5M
        [1e3, 'K']    //            25,000 -> 25K
    ];

    /* Base currencies whose countries group digits the Indian way (lakh / crore).
       Any other base currency uses the international K / M / B scale. */
    var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

    VIS.Util.usesIndianNumbering = function (isoCode) {
        return INDIAN_NUMBERING_CURRENCIES.indexOf(String(isoCode || '').toUpperCase()) >= 0;
    };

    VIS.Util.formatCompactAmount = function (value, isoCode, precision) {
        if (precision === undefined || precision === null) {
            precision = VIS.Env.getCtx().getStdPrecision();
        }
        var tiers = VIS.Util.usesIndianNumbering(isoCode) ? INDIAN_TIERS : INTL_TIERS;
        var absVal = Math.abs(Number(value) || 0);

        for (var i = 0; i < tiers.length; i++) {
            if (absVal >= tiers[i][0]) {
                return (absVal / tiers[i][0]).toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision }) + tiers[i][1];
            }
        }
        return absVal.toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision });
    };

})(VIS);

/* Format an amount in the browser's culture.
   VAS.formatAmountByBrowser(value, isoCode, precision, { compact, symbol }) - see the file header.
   The shared definition (20-Nov-2026). Widgets and panels written before it carry a guarded copy
   of the same function ("define if missing"); those load earlier in the bundle, so this one is
   assigned WITHOUT the guard and is the one in use. The copies can be removed from those files. */
; (function () {
    var V = window.VAS = window.VAS || {};

    function browserLocale() { try { return window.navigator.language || undefined; } catch (e) { return undefined; } }

    /* Precision: the given one, else the context's StdPrecision, else 2; at most 10. */
    function resolvePrecision(p) {
        var n = Number(p);
        if (p === null || p === undefined || p === '' || isNaN(n) || n < 0) {
            try { n = VIS.Env.getCtx().getStdPrecision(); } catch (e) { n = 2; }
        }
        n = Math.round(Number(n));
        return (isNaN(n) || n < 0) ? 2 : Math.min(n, 10);
    }

    function validIso(c) { c = String(c || '').trim().toUpperCase(); return /^[A-Z]{3}$/.test(c) ? c : ''; }

    /* Intl.NumberFormat in the browser culture; an engine without narrowSymbol falls back to symbol. */
    function numberFormat(o) {
        try { return new Intl.NumberFormat(browserLocale(), o); } catch (e) { }
        if (o.currencyDisplay === 'narrowSymbol') {
            o.currencyDisplay = 'symbol';
            try { return new Intl.NumberFormat(browserLocale(), o); } catch (e2) { }
        }
        return null;
    }

    /* Currency sign and where the browser culture puts it for this ISO code (before / after the
       number, and the space between), read once per culture + currency from formatToParts. */
    var layoutCache = {};
    function currencyLayout(iso) {
        var key = browserLocale() + '|' + iso;
        if (layoutCache[key]) return layoutCache[key];
        var out = { sign: '', before: true, gap: '' };
        var f = iso ? numberFormat({ style: 'currency', currency: iso, currencyDisplay: 'narrowSymbol' }) : null;
        if (f && typeof f.formatToParts === 'function') {
            try {
                var parts = f.formatToParts(1234.5), ci = -1, ni = -1;
                for (var i = 0; i < parts.length; i++) {
                    if (parts[i].type === 'currency' && ci < 0) ci = i;
                    if (parts[i].type === 'integer' && ni < 0) ni = i;
                }
                if (ci >= 0) {
                    out.sign = parts[ci].value;
                    if (ni >= 0) {
                        out.before = ci < ni;
                        var near = out.before ? parts[ci + 1] : parts[ci - 1];
                        out.gap = (near && near.type === 'literal') ? near.value : '';
                    }
                }
            } catch (e) { }
        }
        layoutCache[key] = out;
        return out;
    }

    V.formatAmountByBrowser = function (value, isoCode, precision, options) {
        options = options || {};
        var num = Number(value) || 0;
        var p = resolvePrecision(precision);
        var iso = validIso(isoCode);
        var body;
        if (options.compact && window.VIS && VIS.Util && typeof VIS.Util.formatCompactAmount === 'function') {
            body = VIS.Util.formatCompactAmount(Math.abs(num), iso, p);
        } else {
            var f = numberFormat({ minimumFractionDigits: p, maximumFractionDigits: p });
            try { body = f ? f.format(Math.abs(num)) : Math.abs(num).toFixed(p); } catch (e) { body = String(Math.abs(num)); }
        }
        // no "-0,00": the minus only when a non-zero digit is shown
        var nonZero = /[1-9]/.test(body);
        var minus = (num < 0 && nonZero) ? (options.minusSign || '-')
            : ((num > 0 && nonZero && options.positiveSign) ? options.positiveSign : '');
        var lay = currencyLayout(iso);
        var sign = lay.sign || options.symbol || iso;
        if (!sign) return minus + body;
        var signOut = (typeof options.symbolWrap === 'function') ? options.symbolWrap(sign) : sign;
        if (!lay.sign) return minus + signOut + ' ' + body;
        return lay.before ? (minus + signOut + lay.gap + body) : (minus + body + lay.gap + signOut);
    };
})();
