/**
 * Completed Today KPI Widget (Material Transfer Dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing total count of stock transfers completed / fully received today.
 *           Renders success text color (#20A464) when count >= 1.
 * Prefix  - VAS_170_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Completed Today                                  | VAS_170_CompletedToday
 *  2  | Fully received                                   | VAS_170_FullyReceived
 *  3  | Unable to load completed today transfers        | VAS_170_UnableToLoadData
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_170_CompletedTodayWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-completed-today-container">');
        var $root = $('<div class="vas-completed-today-root">');
        var $valueEl;
        var $metaEl;
        var $busy;
        var widgetObserver = null;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-completed-today-hidden', !show);
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language);
        }

        function escapeHtml(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function setupWidgetSizeObserver() {
            if (typeof ResizeObserver === 'undefined') { return; }
            widgetObserver = new ResizeObserver(function (entries) {
                for (var i = 0; i < entries.length; i++) {
                    var width = entries[i].contentRect.width;
                    if (width > 0) {
                        $root[0].style.setProperty('--widget-inline-size', width + 'px');
                    }
                }
            });
            widgetObserver.observe($wrapper[0]);
        }


        /* DocStatus list and date filter — mirrors the backend predicate exactly
           so the drill-through list row count always equals the KPI number. */
        var COMPLETED_STATUS_LIST = "'CO', 'CL'";

        function todayIso() {
            var d = new Date();
            return d.getFullYear() + '-' +
                   String(d.getMonth() + 1).padStart(2, '0') + '-' +
                   String(d.getDate()).padStart(2, '0');
        }

        function openTransferList() {
            var today = todayIso();
            var where =
                "MMovement.IsActive = 'Y'" +
                " AND MMovement.DocStatus IN (" + COMPLETED_STATUS_LIST + ")" +
                " AND TRUNC(MMovement.Updated) = TO_DATE('" + today + "','YYYY-MM-DD')";

            var windowParam = {
                "TabWhereClause": where,
                "TabLayout": "N",
                "TabIndex": "0"
            };
            $self.widgetFirevalueChanged(windowParam);
        }

        this.Initalize = function () {
            createWidget();
            loadKpi();
        };

        function loadKpi() {
            showBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_170_CompletedTodayWidget/GetCompletedTodayData',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'string') { data = JSON.parse(data); }

                    if (data && data.error) { setError(); return; }

                    renderMetric(data || {});
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

// ===== NEW CODE START — currency format (agent C04, 2026-08-19) =====
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
            var count = Number(data.count || 0);
            if (data.currency) {
                currencyInfo = data.currency;
            }

            if ($valueEl) {
                $valueEl.text(formatCount(count));
                $valueEl.attr('title', count.toLocaleString(window.navigator.language));
                $valueEl.attr('aria-live', 'polite');

                // Success tone (#20A464) when count >= 1, neutral (#102C3F) when 0
                if (count >= 1) {
                    $valueEl.addClass('vas-completed-today-success');
                } else {
                    $valueEl.removeClass('vas-completed-today-success');
                }
            }

            if ($metaEl) {
                var metaMsg = lbl("VAS_170_FullyReceived", "Fully received");
                $metaEl.text(metaMsg);
                $metaEl.attr('title', metaMsg);
            }
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
/*
        function renderMetric(data) {
            var count = Number(data.count || 0);

            if ($valueEl) {
                $valueEl.text(formatCount(count));
                $valueEl.attr('title', formatCount(count));
                $valueEl.attr('aria-live', 'polite');

                // Success tone (#20A464) when count >= 1, neutral (#102C3F) when 0
                if (count >= 1) {
                    $valueEl.addClass('vas-completed-today-success');
                } else {
                    $valueEl.removeClass('vas-completed-today-success');
                }
            }

            if ($metaEl) {
                var metaMsg = lbl("VAS_170_FullyReceived", "Fully received");
                $metaEl.text(metaMsg);
                $metaEl.attr('title', metaMsg);
            }
        }
*/
// ----- END OLD CODE -----

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
                $valueEl.removeClass('vas-completed-today-success');
            }
            if ($metaEl) {
                var errText = lbl("VAS_170_UnableToLoadData", "Unable to load completed today transfers");
                $metaEl.text(errText);
                $metaEl.attr('title', errText);
            }
        }

        function createWidget() {
            var title = lbl("VAS_170_CompletedToday", "Completed Today");
            var metaText = lbl("VAS_170_FullyReceived", "Fully received");

            var $card = $(
                '<button type="button" class="vas-completed-today-card" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-completed-today-label">' + escapeHtml(title) + '</div>' +
                '<div class="vas-completed-today-value">—</div>' +
                '<div class="vas-completed-today-meta">' + escapeHtml(metaText) + '</div>' +
                '</button>'
            );

            $card.on('click', function () { openTransferList(); });

            $valueEl = $card.find('.vas-completed-today-value');
            $metaEl = $card.find('.vas-completed-today-meta');

            $root.append($card);

            $busy = $('<div class="vas-completed-today-busy vas-completed-today-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($busy);

            $wrapper.append($root);
            setupWidgetSizeObserver();
        }

        this.refreshWidget = function () {
            loadKpi();
        };

        this.getRoot = function () { return $wrapper; };

        this.disposeComponent = function () {
            if (widgetObserver && $wrapper[0]) {
                widgetObserver.unobserve($wrapper[0]);
                widgetObserver = null;
            }
            $root.off();
            $root.remove();
            $wrapper.remove();
        };
    };

    VAS.VAS_170_CompletedTodayWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_170_CompletedTodayWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_170_CompletedTodayWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_170_CompletedTodayWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_170_CompletedTodayWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_170_CompletedTodayWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
