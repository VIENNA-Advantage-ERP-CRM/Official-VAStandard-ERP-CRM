/**
 * Browser-culture amount formatter for dashboard KPI widgets
 * (Customer Master, Service Contract and Sales Order windows).
 *
 * VIS.Util.BrowserCultureFormatter.formatCompactAmount(value, isoCode, precision)
 *   Formats the *magnitude* of a currency amount into a compact string in the
 *   BROWSER culture (window.navigator.language) - see QueryRules.md, "Date, Time and
 *   Amount Format - Browser Culture" and "Compact Amount and Window Zoom".
 *
 *   The compact unit (K / M / B, Mrd., Lakh / Crore, 万 / 億 ...), the digits, the decimal
 *   mark and the grouping all come from the browser culture through
 *   Intl.NumberFormat({ notation: 'compact' }). They are NOT chosen from the currency:
 *     • en-US   2,500,000 -> 2.5M        • en-IN   2,50,000 -> 2.5L, 5,00,00,000 -> 5Cr
 *     • de-DE   2,500,000 -> 2,5 Mio.    • ja-JP   250,000,000 -> 2.5億
 *   The fraction (and any sub-thousand amount) is kept to the currency precision.
 *   The returned string is always non-negative - callers prepend their own sign and
 *   currency symbol so each widget keeps its own composition.
 *
 * VIS.Util.BrowserCultureFormatter.formatFullAmount(value, isoCode, precision)
 *   The FULL (non-compact) amount in the browser culture, for the hover tooltip of a
 *   compact amount.
 *
 * This is a separate file from CurrencyFormat.js (which is left unchanged and keeps
 * the base-currency K / L / Cr / M / B scale for the other windows).
 */
; VIS = window.VIS || {};

; (function (VIS) {

    VIS.Util = VIS.Util || {};

    /* One formatter per (culture, precision) - a dashboard formats many values. */
    var formatterCache = {};

    function browserLocale() {
        try { return window.navigator.language || undefined; } catch (e) { return undefined; }
    }

    function compactFormatter(locale, precision) {
        var key = (locale || '') + '|' + precision;
        if (!formatterCache[key]) {
            formatterCache[key] = new Intl.NumberFormat(locale, {
                notation: 'compact',
                compactDisplay: 'short',
                /* Compact notation defaults to "min2" grouping, which leaves 4-digit
                   numbers ungrouped in some cultures (de-DE "5268,70" vs the full amount's
                   "5.268,70"). 'always' keeps the grouping identical to the full amount. */
                useGrouping: 'always',
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
        }
        return formatterCache[key];
    }

    function normalisePrecision(precision) {
        if (precision === undefined || precision === null) {
            precision = VIS.Env.getCtx().getStdPrecision();
        }
        return Math.max(0, Math.min(20, Math.round(Number(precision)) || 0));
    }

    var BrowserCultureFormatter = {

        /* Retained for callers that still ask; the compact scale does not depend on the currency. */
        usesIndianNumbering: function (isoCode) {
            return ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'].indexOf(String(isoCode || '').toUpperCase()) >= 0;
        },

        /**
         * The FULL (non-compact) amount in the browser culture, with the currency sign placed
         * per the culture and the decimals kept to the currency precision - e.g. en-US
         * "$1,125,430,000.00", de-DE "1.125.430.000,00 $". The value keeps its sign; with no
         * valid ISO code only the number is returned.
         */
        formatFullAmount: function (value, isoCode, precision) {
            precision = normalisePrecision(precision);
            var num = Number(value) || 0;
            var locale = browserLocale();
            var iso = String(isoCode || '').trim().toUpperCase();
            var opts = { minimumFractionDigits: precision, maximumFractionDigits: precision };
            try {
                if (/^[A-Z]{3}$/.test(iso)) {
                    opts.style = 'currency';
                    opts.currency = iso;
                }
                return num.toLocaleString(locale, opts);
            } catch (e) {
                delete opts.style; delete opts.currency;
                return num.toLocaleString(locale, opts);
            }
        },

        /** Compact magnitude (non-negative) in the browser culture. */
        formatCompactAmount: function (value, isoCode, precision) {
            precision = normalisePrecision(precision);
            var absVal = Math.abs(Number(value) || 0);
            var locale = browserLocale();

            try {
                return compactFormatter(locale, precision).format(absVal);
            } catch (e) {
                /* Browser without compact notation: full number in the browser culture. */
                return absVal.toLocaleString(locale, { minimumFractionDigits: precision, maximumFractionDigits: precision });
            }
        }
    };

    VIS.Util.BrowserCultureFormatter = BrowserCultureFormatter;

})(VIS);
