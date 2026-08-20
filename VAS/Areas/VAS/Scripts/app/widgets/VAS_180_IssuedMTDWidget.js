/**
 * VAS_180_IssuedMTDWidget
 * 2x1 KPI tile for Inventory Use dashboard.
 * Displays aggregate count of material issue lines posted Month-to-Date (MTD).
 *
 * Summary Message Table
 *  # | Current Text                    | Message Key
 * ---+---------------------------------+-----------------------------------
 *  1 | Issued MTD                      | VAS_180_IssuedMTD
 *  2 | Issue lines this month          | VAS_180_IssueLinesThisMonth
 *  3 | No issue lines MTD              | VAS_180_NoIssueLinesMTD
 *  4 | Couldn't load                   | VAS_180_CouldntLoad
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

    VAS.VAS_180_IssuedMTDWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-imtd-root">');
        var $card;
        var $valueEl;
        var $metaEl;
        var $busy;
        var lastCount = 0;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return (translated && translated.charAt(0) !== '[') ? translated : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function parseResponse(res) {
            var data = res;
            if (typeof data === 'string') { data = JSON.parse(data); }
            if (typeof data === 'string') { data = JSON.parse(data); }
            return data || {};
        }

// ===== NEW CODE START — currency format (agent A02, 2026-08-19) =====
        var currencyInfo = { iso: '', symbol: '' };

        function formatCount(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        function formatCurrency(amount, currInfo) {
            var val = Number(amount || 0);
            var info = currInfo || currencyInfo || {};
            var iso = (info.iso || '').toUpperCase();
            var symbol = info.symbol || '';

            var indianISOs = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'];
            var isIndian = indianISOs.indexOf(iso) !== -1;

            var formatted = '';
            var absVal = Math.abs(val);

            if (isIndian) {
                if (absVal >= 10000000) {
                    formatted = (val / 10000000).toFixed(2).replace(/\.00$/, '') + ' Cr';
                } else if (absVal >= 100000) {
                    formatted = (val / 100000).toFixed(2).replace(/\.00$/, '') + ' Lk';
                } else {
                    var parts = val.toFixed(2).replace(/\.00$/, '').split('.');
                    var integerPart = parts[0];
                    var decimalPart = parts.length > 1 ? '.' + parts[1] : '';
                    var isNeg = integerPart.charAt(0) === '-';
                    if (isNeg) integerPart = integerPart.substring(1);

                    if (integerPart.length > 3) {
                        var lastThree = integerPart.substring(integerPart.length - 3);
                        var otherNumbers = integerPart.substring(0, integerPart.length - 3);
                        otherNumbers = otherNumbers.replace(/\B(?=(\d{2})+(?!\d))/g, ",");
                        integerPart = otherNumbers + "," + lastThree;
                    }
                    formatted = (isNeg ? '-' : '') + integerPart + decimalPart;
                }
            } else {
                if (absVal >= 1000000000) {
                    formatted = (val / 1000000000).toFixed(2).replace(/\.00$/, '') + 'B';
                } else if (absVal >= 1000000) {
                    formatted = (val / 1000000).toFixed(2).replace(/\.00$/, '') + 'M';
                } else {
                    var parts = val.toFixed(2).replace(/\.00$/, '').split('.');
                    parts[0] = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ",");
                    formatted = parts.join('.');
                }
            }

            if (symbol) {
                return symbol + ' ' + formatted;
            }
            return formatted;
        }

        function renderMetric(data) {
            lastCount = Number(data.count || 0);
            if (data.currency) {
                currencyInfo = data.currency;
            }

            if ($valueEl) {
                var formattedVal = formatCount(lastCount);
                $valueEl.text(formattedVal);
                $valueEl.attr('title', String(lastCount));
            }
            if ($metaEl) {
                $metaEl.text(lastCount === 0
                    ? label("VAS_180_NoIssueLinesMTD", "No issue lines MTD")
                    : label("VAS_180_IssueLinesThisMonth", "Issue lines this month"));
            }
            if ($card) { $card.prop('disabled', false); }
        }

        function setError() {
            lastCount = 0;
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) { $metaEl.text(label("VAS_180_CouldntLoad", "Couldn't load")); }
            if ($card) { $card.prop('disabled', true); }
        }

        function openIssuedMTDList() {
            // Keep in lock-step with GetIssuedMTDCountData in the controller. This drills through
            // at DOCUMENT level, so it can carry the IsInternalUse filter but not the line-level
            // QtyInternalUse one - it lands on the issue documents behind the counted lines.
            var where = "M_Inventory.IsActive = 'Y' AND M_Inventory.DocStatus IN ('CO', 'CL') AND COALESCE(M_Inventory.IsInternalUse, 'N') = 'Y' AND M_Inventory.MovementDate >= TRUNC(SYSDATE, 'MM') AND M_Inventory.MovementDate < ADD_MONTHS(TRUNC(SYSDATE, 'MM'), 1)";
            var windowParam = {
                "TabWhereClause": where,
                "TabLayout": "N",
                "TabIndex": "0",
                "ActionName": "VAS_InternalUseInventory",
                "ActionType": "W"
            };
            $self.widgetFirevalueChanged(windowParam);
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
//        function formatCount(value) {
//            var n = Number(value || 0);
//            return n.toLocaleString(window.navigator.language);
//        }
//
//        function renderMetric(data) {
//            lastCount = Number(data.count || 0);
//
//            if ($valueEl) {
//                $valueEl.text(formatCount(lastCount));
//                $valueEl.attr('title', formatCount(lastCount));
//            }
//            if ($metaEl) {
//                $metaEl.text(lastCount === 0
//                    ? label("VAS_NoIssueLinesMTD", "No issue lines MTD")
//                    : label("VAS_IssueLinesThisMonth", "Issue lines this month"));
//            }
//            if ($card) { $card.prop('disabled', false); }
//        }
//
//        function openIssuedMTDList() {
//            var where = "M_Inventory.IsActive = 'Y' AND M_Inventory.DocStatus IN ('CO', 'CL') AND M_Inventory.MovementDate >= TRUNC(SYSDATE, 'MM') AND M_Inventory.MovementDate < ADD_MONTHS(TRUNC(SYSDATE, 'MM'), 1)";
//            var windowParam = {
//                "TabWhereClause": where,
//                "TabLayout": "N",
//                "TabIndex": "0"
//            };
//            $self.widgetFirevalueChanged(windowParam);
//        }
// ----- END OLD CODE -----

        function createWidget() {
            var title = label("VAS_180_IssuedMTD", "Issued MTD");
            $card = $(
                '<button type="button" class="vas-imtd-card vas-widget-bg" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-imtd-label">' + escapeHtml(title) + '</div>' +
                '<div class="vas-imtd-value">—</div>' +
                '<div class="vas-imtd-meta"></div>' +
                '</button>'
            );

            $valueEl = $card.find('.vas-imtd-value');
            $metaEl = $card.find('.vas-imtd-meta');

            $card.on('click', function () { openIssuedMTDList(); });
            $root.append($card);

            $busy = $('<div class="vas-imtd-busy vas-imtd-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($card) { $card.off('click'); }
            $root.remove();
        };
    };

    VAS.VAS_180_IssuedMTDWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_180_IssuedMTDWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_180_IssuedMTDWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_180_IssuedMTDWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_180_IssuedMTDWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_180_IssuedMTDWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
