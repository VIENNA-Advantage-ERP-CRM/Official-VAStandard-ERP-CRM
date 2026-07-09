/**
 * Total Inventory Value Widget
 * Summary Message Table
 *  # | Current Text          | Message Key
 * ---+-----------------------+-------------------------
 *  1 | Total Inventory Value | VAS_TotalInventoryValue
 *  2 | On-hand valuation     | VAS_OnHandValuation
 *  3 | Couldn't load         | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_075_TotalInventoryValueWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-inventory-value-root">');
        var $value;
        var $meta;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
        }

        function getPrecision(value) {
            var precision = Number(value);
            if (!isNaN(precision) && precision >= 0) { return precision; }
            if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                precision = Number(VIS.Env.getCtx().getStdPrecision());
            }
            return !isNaN(precision) && precision >= 0 ? precision : 0;
        }

        // Review #8 (common): currencies of Indian-numbering countries get Indian
        // digit grouping and Lakh/Crore compact notation; all others get
        // international grouping and K/M/B. The symbol always comes from the DB.
        var INDIAN_NUMBERING_CURRENCIES = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];

        function usesIndianNumbering(isoCode) {
            return INDIAN_NUMBERING_CURRENCIES.indexOf(String(isoCode || '').toUpperCase()) >= 0;
        }

        function currencyLocale(isoCode) {
            return usesIndianNumbering(isoCode) ? 'en-IN' : 'en-US';
        }

        function trimTrailingZeros(text) {
            return text.replace(/\.0+$/, '').replace(/(\.\d*?)0+$/, '$1');
        }

        function formatCompactNumber(value, isoCode) {
            var number = Number(value || 0);
            if (!isFinite(number)) { number = 0; }
            var abs = Math.abs(number);
            if (usesIndianNumbering(isoCode)) {
                if (abs >= 10000000) { return trimTrailingZeros((number / 10000000).toFixed(2)) + ' Cr'; }
                if (abs >= 100000) { return trimTrailingZeros((number / 100000).toFixed(2)) + ' Lakh'; }
                if (abs >= 1000) { return trimTrailingZeros((number / 1000).toFixed(1)) + 'K'; }
            } else {
                if (abs >= 1000000000) { return trimTrailingZeros((number / 1000000000).toFixed(1)) + 'B'; }
                if (abs >= 1000000) { return trimTrailingZeros((number / 1000000).toFixed(1)) + 'M'; }
                if (abs >= 1000) { return trimTrailingZeros((number / 1000).toFixed(1)) + 'K'; }
            }
            return number.toLocaleString(currencyLocale(isoCode), { maximumFractionDigits: 2 });
        }

        function formatAmount(value, symbol, isoCode, precision) {
            var number = Number(value || 0);
            if (!isFinite(number)) { number = 0; }
            var currency = symbol || isoCode || '';
            var stdPrecision = getPrecision(precision);
            var formatted = number.toLocaleString(currencyLocale(isoCode), {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            return currency ? currency + ' ' + formatted : formatted;
        }

        function loadValue() {
            $value.text('\u2014');
            $meta.text(label('VAS_OnHandValuation', 'On-hand valuation'));

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_075_TotalInventoryValueWidget/GetTotalInventoryValue',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (result && !result.error) {
                        var formatted = formatAmount(
                            result.total_inventory_value,
                            result.currency_symbol,
                            result.currency_iso,
                            result.std_precision
                        );
                        // KPI tile shows the compact form (₹3.42 Cr / $34.2M);
                        // the tooltip keeps the exact amount.
                        var compact = (result.currency_symbol || result.currency_iso || '')
                            + formatCompactNumber(result.total_inventory_value, result.currency_iso);
                        $value.text(compact).attr('title', formatted);
                        return;
                    }

                    showError();
                },
                error: showError
            });
        }

        function showError() {
            $value.text('\u2014');
            $meta.text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        this.Initalize = function () {
            var $card = $(
                '<div class="MPC-inventory-value-card" aria-live="polite">' +
                    '<div class="MPC-inventory-value-label"></div>' +
                    '<div class="MPC-inventory-value-value">\u2014</div>' +
                    '<div class="MPC-inventory-value-meta"></div>' +
                '</div>'
            );

            $card.find('.MPC-inventory-value-label').text(label('VAS_TotalInventoryValue', 'Total Inventory Value'));
            $value = $card.find('.MPC-inventory-value-value');
            $meta = $card.find('.MPC-inventory-value-meta');
            $root.append($card);
            loadValue();
        };

        this.refreshWidget = function () {
            loadValue();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VAS.VAS_075_TotalInventoryValueWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_075_TotalInventoryValueWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_075_TotalInventoryValueWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_075_TotalInventoryValueWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
