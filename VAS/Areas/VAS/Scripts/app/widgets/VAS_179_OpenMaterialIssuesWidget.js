/**
 * VAS_179_OpenMaterialIssuesWidget
 * 2x1 KPI tile for Inventory Use dashboard.
 * Displays aggregate count of open material issue documents (awaiting fulfillment).
 *
 * Summary Message Table
 *  # | Current Text                    | Message Key
 * ---+---------------------------------+-----------------------------------
 *  1 | Open Issues                     | VAS_179_OpenMaterialIssues
 *  2 | Awaiting fulfillment            | VAS_179_AwaitingFulfillment
 *  3 | No open issues                  | VAS_179_NoOpenMaterialIssues
 *  4 | Couldn't load                   | VAS_179_CouldntLoad
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

    VAS.VAS_179_OpenMaterialIssuesWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-omi-root">');
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

        function formatCount(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-omi-hidden', !show);
        }

        this.Initalize = function () {
            createWidget();
            setupResizeObserver();
            loadKpi();
        };

        function setupResizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            try {
                var ro = new ResizeObserver(function (entries) {
                    for (var i = 0; i < entries.length; i++) {
                        var width = entries[i].contentRect.width;
                        if (width > 0 && $root[0]) {
                            $root[0].style.setProperty('--widget-inline-size', width + 'px');
                        }
                    }
                });
                ro.observe($root[0]);
            } catch (e) { }
        }

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_179_OpenMaterialIssuesWidget/GetOpenMaterialIssuesCount',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error) { setError(); return; }
                    renderMetric(data);
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

        // ===== NEW CODE START — currency format (agent A01, 2026-08-19) =====
        var currencyInfo = { iso: "", symbol: "" };

        function formatCurrency(val, currencyObj) {
            var amount = Number(val || 0);
            if (isNaN(amount)) { amount = 0; }
            var cur = currencyObj || currencyInfo || {};
            var symbol = cur.symbol || "";
            var iso = (cur.iso || "").toUpperCase();

            var indianISOs = ["INR", "PKR", "BDT", "NPR", "BTN", "LKR"];
            var formattedNum = "";

            if (indianISOs.indexOf(iso) !== -1) {
                var absAmt = Math.abs(amount);
                var sign = amount < 0 ? "-" : "";
                if (absAmt >= 10000000) {
                    formattedNum = sign + (absAmt / 10000000).toFixed(2).replace(/\.00$/, '') + ' Cr';
                } else if (absAmt >= 100000) {
                    formattedNum = sign + (absAmt / 100000).toFixed(2).replace(/\.00$/, '') + ' L';
                } else {
                    var parts = absAmt.toFixed(2).split('.');
                    var numStr = parts[0];
                    var decStr = parts[1];
                    if (numStr.length > 3) {
                        var last3 = numStr.substring(numStr.length - 3);
                        var otherDigits = numStr.substring(0, numStr.length - 3);
                        formattedNum = otherDigits.replace(/\B(?=(\d{2})+(?!\d))/g, ",") + "," + last3;
                    } else {
                        formattedNum = numStr;
                    }
                    if (decStr && decStr !== "00") {
                        formattedNum += "." + decStr;
                    }
                    formattedNum = sign + formattedNum;
                }
            } else {
                var absAmt = Math.abs(amount);
                var sign = amount < 0 ? "-" : "";
                if (absAmt >= 1000000000) {
                    formattedNum = sign + (absAmt / 1000000000).toFixed(2).replace(/\.00$/, '') + 'B';
                } else if (absAmt >= 1000000) {
                    formattedNum = sign + (absAmt / 1000000).toFixed(2).replace(/\.00$/, '') + 'M';
                } else {
                    formattedNum = amount.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 });
                }
            }

            return symbol ? (symbol + " " + formattedNum).trim() : formattedNum;
        }

        function formatCount(value) {
            var n = Number(value || 0);
            return n.toLocaleString(window.navigator.language);
        }

        function renderMetric(data) {
            lastCount = Number(data.count || 0);
            if (data.currency) {
                currencyInfo = data.currency;
            }

            if ($valueEl) {
                $valueEl.text(formatCount(lastCount));
                $valueEl.attr('title', lastCount.toLocaleString(window.navigator.language));
            }
            if ($metaEl) {
                $metaEl.text(lastCount === 0
                    ? label("VAS_179_NoOpenMaterialIssues", "No open issues")
                    : label("VAS_179_AwaitingFulfillment", "Awaiting fulfillment"));
            }
            if ($card) { $card.prop('disabled', false); }
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
//                    ? label("VAS_NoOpenMaterialIssues", "No open issues")
//                    : label("VAS_AwaitingFulfillment", "Awaiting fulfillment"));
//            }
//            if ($card) { $card.prop('disabled', false); }
//        }
// ----- END OLD CODE -----

        function setError() {
            lastCount = 0;
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) { $metaEl.text(label("VAS_179_CouldntLoad", "Couldn't load")); }
            if ($card) { $card.prop('disabled', true); }
        }

        function openMaterialIssuesList() {
            // Must stay in lock-step with GetOpenMaterialIssuesCountData in the controller -
            // including IsInternalUse, or the drilled-through list shows physical inventory
            // documents the KPI never counted.
            var where = "M_Inventory.IsActive = 'Y' AND M_Inventory.IsInternalUse = 'Y' AND M_Inventory.DocStatus IN ('DR', 'IP', 'WC', 'IN')";
            var windowParam = {
                "TabWhereClause": where,
                "TabLayout": "N",
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        function createWidget() {
            var title = label("VAS_179_OpenMaterialIssues", "Open Issues");
            $card = $(
                '<button type="button" class="vas-omi-card vas-widget-bg" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-omi-label">' + escapeHtml(title) + '</div>' +
                '<div class="vas-omi-value">—</div>' +
                '<div class="vas-omi-meta"></div>' +
                '</button>'
            );

            $valueEl = $card.find('.vas-omi-value');
            $metaEl = $card.find('.vas-omi-meta');

            $card.on('click', function () { openMaterialIssuesList(); });
            $root.append($card);

            $busy = $('<div class="vas-omi-busy vas-omi-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

    VAS.VAS_179_OpenMaterialIssuesWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_179_OpenMaterialIssuesWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_179_OpenMaterialIssuesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_179_OpenMaterialIssuesWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_179_OpenMaterialIssuesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_179_OpenMaterialIssuesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
