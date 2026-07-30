/**
 * Shared currency-formatting helper for dashboard KPI widgets.
 *
 * VIS.Util.formatCompactAmount(value, isoCode, precision)
 *   Formats the *magnitude* of a currency amount into a compact, locale-aware
 *   string. The numbering system depends on the base (accounting-schema) currency:
 *
 *     • INR  → Indian system:        Thousand(K), Lakh(L), Crore(Cr), Arab(Ar)
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

    VIS.Util.formatCompactAmount = function (value, isoCode, precision) {
        if (precision === undefined || precision === null) {
            precision = VIS.Env.getCtx().getStdPrecision();
        }
        var tiers = (isoCode === 'INR') ? INDIAN_TIERS : INTL_TIERS;
        var absVal = Math.abs(Number(value) || 0);

        for (var i = 0; i < tiers.length; i++) {
            if (absVal >= tiers[i][0]) {
                return (absVal / tiers[i][0]).toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision }) + tiers[i][1];
            }
        }
        return absVal.toLocaleString(window.navigator.language, { minimumFractionDigits: precision, maximumFractionDigits: precision });
    };

})(VIS);
