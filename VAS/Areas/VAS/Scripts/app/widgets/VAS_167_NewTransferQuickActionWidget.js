/**
 * New Transfer Quick Action Widget
 * 1x1 quick-action tile launching the Material Transfer window on a NEW record
 * via the widget framework's value-changed channel (IsTabInNewMode).
 * ID Prefix: VAS_167_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | New Transfer                                     | VAS_167_NewTransfer
 *  2  | Move stock between sites                         | VAS_167_MoveStockBetweenSites
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VAS.VAS_167_NewTransferQuickActionWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-new-transfer-quick-action-root">');

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

// ===== NEW CODE START — currency format (agent C01, 2026-08-19) =====
        /**
         * Formats a monetary value according to the organization's currency ISO code and symbol.
         * @param {number|string} val - Raw monetary amount
         * @param {Object} currencyInfo - Currency meta object { iso: string, symbol: string }
         * @returns {string} Formatted string
         */
        function formatCurrency(val, currencyInfo) {
            var iso = (currencyInfo && currencyInfo.iso) ? String(currencyInfo.iso).toUpperCase() : '';
            var symbol = (currencyInfo && currencyInfo.symbol) ? String(currencyInfo.symbol) : '';
            var num = Number(val);
            if (val === null || val === undefined || isNaN(num)) {
                num = 0;
            }
            var absNum = Math.abs(num);
            var isNegative = num < 0;
            var formattedStr = '';

            var isIndian = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'].indexOf(iso) !== -1;

            if (isIndian) {
                if (absNum >= 10000000) {
                    formattedStr = (absNum / 10000000).toFixed(2).replace(/\.00$/, '').replace(/(\.\d)0$/, '$1') + ' Cr';
                } else if (absNum >= 100000) {
                    formattedStr = (absNum / 100000).toFixed(2).replace(/\.00$/, '').replace(/(\.\d)0$/, '$1') + ' Lakh';
                } else {
                    var parts = absNum.toFixed(2).split('.');
                    var intPart = parts[0];
                    var decPart = parts[1] === '00' ? '' : '.' + parts[1];
                    var lastThree = intPart.substring(intPart.length - 3);
                    var otherNumbers = intPart.substring(0, intPart.length - 3);
                    if (otherNumbers !== '') {
                        lastThree = ',' + lastThree;
                    }
                    formattedStr = otherNumbers.replace(/\B(?=(\d{2})+(?!\d))/g, ",") + lastThree + decPart;
                }
            } else {
                if (absNum >= 1000000000) {
                    formattedStr = (absNum / 1000000000).toFixed(2).replace(/\.00$/, '').replace(/(\.\d)0$/, '$1') + 'B';
                } else if (absNum >= 1000000) {
                    formattedStr = (absNum / 1000000).toFixed(2).replace(/\.00$/, '').replace(/(\.\d)0$/, '$1') + 'M';
                } else {
                    var parts = absNum.toFixed(2).split('.');
                    var decPart = parts[1] === '00' ? '' : '.' + parts[1];
                    formattedStr = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ",") + decPart;
                }
            }

            return (isNegative ? '-' : '') + (symbol ? symbol + ' ' : '') + formattedStr;
        }
// ===== NEW CODE END — currency format =====

// ----- OLD CODE (kept for rollback, do not delete) -----
// (No prior currency formatting function existed in this quick action tile)
// ----- END OLD CODE -----

        // Open the widget's configured window directly on a NEW record
        // through the widget framework's value-changed channel.
        function openNewTransfer() {
            var windowParam = {
                "IsTabInNewMode": "true",
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        function createWidget() {
            var title = lbl('VAS_167_NewTransfer', 'New Transfer');
            var subtitle = lbl('VAS_167_MoveStockBetweenSites', 'Move stock between sites');

            var $card = $(
                '<button type="button" class="vas-new-transfer-quick-action-card" aria-label="' + escapeHtml(title) + '">' +
                    '<span class="vas-new-transfer-quick-action-iconrow">' +
                        '<span class="vas-new-transfer-quick-action-ico">' +
                            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>' +
                        '</span>' +
                    '</span>' +
                    '<span class="vas-new-transfer-quick-action-text">' +
                        '<span class="vas-new-transfer-quick-action-title">' + escapeHtml(title) + '</span>' +
                        '<span class="vas-new-transfer-quick-action-sub">' + escapeHtml(subtitle) + '</span>' +
                    '</span>' +
                '</button>'
            );

            $card.on('click', function () { openNewTransfer(); });
            $root.append($card);
        }

        this.Initalize = function () {
            createWidget();
        };

        this.refreshWidget = function () { };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $root.off();
            $root.remove();
        };
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_167_NewTransferQuickActionWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
