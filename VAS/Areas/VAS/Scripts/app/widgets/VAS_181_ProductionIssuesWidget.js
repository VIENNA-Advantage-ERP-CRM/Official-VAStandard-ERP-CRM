/**
 * VAS_181_ProductionIssuesWidget
 * 2x1 KPI tile for Inventory Use dashboard.
 * Displays percentage share of material issue value for Production Month-to-Date (MTD).
 *
 * Summary Message Table
 *  # | Current Text                    | Message Key
 * ---+---------------------------------+-----------------------------------
 *  1 | Production Issues               | VAS_181_ProductionIssues
 *  2 | Of issued value MTD             | VAS_181_OfIssuedValueMTD
 *  3 | Couldn't load                   | VAS_181_CouldntLoad
 *
 * NOTE (2026-09-18, Claude): setupResizeObserver()/--widget-inline-size removed from
 * Initalize() to match VAS_180_IssuedMTDWidget's label/value/meta size. VAS_180 never
 * scopes --widget-inline-size to its own card, so its font-size clamp() falls through
 * to the dashboard-wide --dash-inline-size and lands near the clamp's midpoint
 * (~18.4px); this widget's own --widget-inline-size was scoped to its ~300px card,
 * which is small enough that the clamp always bottomed out at its 16px floor instead.
 * Also found while here: the "OLD CODE (kept for rollback, do not delete)" Initalize
 * block below this one is NOT actually commented out - it re-assigns this.Initalize
 * and, being the later assignment, silently wins over the "NEW CODE" version above it
 * (so loadCurrencyInfo() was never being called). Left as-is / out of scope for this
 * change beyond removing setupResizeObserver() from both, since fixing it changes
 * runtime behavior beyond what was asked here - flagged for a separate task.
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

    VAS.VAS_181_ProductionIssuesWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $root = $('<div class="vas-piw-root">');
        var $card;
        var $valueEl;
        var $metaEl;
        var $busy;

        function label(key, fallback) {
            return VIS.Msg.getMsg(key);
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

        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy.toggleClass('vas-piw-hidden', !show);
        }

// ===== NEW CODE START — currency format (agent A03, 2026-08-19) =====
        var currencyInfo = { iso: '', symbol: '' };
        // Work-order columns this installation actually has, reported by the KPI endpoint
        // (they are manufacturing-module only and are absent on some databases).
        var workOrderColumns = [];

        function loadCurrencyInfo() {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_181_ProductionIssuesWidget/GetCurrencyInfo',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data && data.iso) {
                        currencyInfo.iso = data.iso;
                        currencyInfo.symbol = data.symbol || '';
                    }
                }
            });
        }

        /**
         * Organization-aware currency formatter
         * @param {number|string} val - Amount to format
         * @param {boolean} compact - Whether to format with Lakh/Crore or M/B
         * @returns {string} Formatted currency string with org currency symbol
         */
        function formatCurrency(val, compact) {
            var num = Number(val);
            if (isNaN(num) || val === null || val === undefined || val === '') {
                num = 0;
            }
            var sym = currencyInfo.symbol || '';
            var iso = (currencyInfo.iso || '').toUpperCase();
            var isIndian = ['INR', 'PKR', 'BDT', 'NPR', 'BTN', 'LKR'].indexOf(iso) !== -1;

            if (compact) {
                var absNum = Math.abs(num);
                var sign = num < 0 ? '-' : '';
                if (isIndian) {
                    if (absNum >= 10000000) {
                        return sym + sign + (absNum / 10000000).toFixed(2) + ' Cr';
                    } else if (absNum >= 100000) {
                        return sym + sign + (absNum / 100000).toFixed(2) + ' L';
                    }
                } else {
                    if (absNum >= 1000000000) {
                        return sym + sign + (absNum / 1000000000).toFixed(2) + ' B';
                    } else if (absNum >= 1000000) {
                        return sym + sign + (absNum / 1000000).toFixed(2) + ' M';
                    }
                }
            }

            var parts = num.toFixed(2).split('.');
            var intPart = parts[0];
            var decPart = parts[1];

            if (isIndian) {
                var lastThree = intPart.substring(intPart.length - 3);
                var otherNumbers = intPart.substring(0, intPart.length - 3);
                if (otherNumbers !== '') {
                    lastThree = ',' + lastThree;
                }
                intPart = otherNumbers.replace(/\B(?=(\d{2})+(?!\d))/g, ",") + lastThree;
            } else {
                intPart = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
            }

            return sym + intPart + '.' + decPart;
        }

        this.Initalize = function () {
            createWidget();
            loadCurrencyInfo();
            loadKpi();
        };
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
        this.Initalize = function () {
            createWidget();
            loadKpi();
        };
// ----- END OLD CODE -----

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
                url: VIS.Application.contextUrl + 'VAS_181_ProductionIssuesWidget/GetProductionIssuesPercentage',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error) { setError(); return; }
                    if (data.workOrderColumns) { workOrderColumns = data.workOrderColumns; }
                    renderMetric(data);
                },
                error: function () { setError(); },
                complete: function () { showBusy(false); }
            });
        }

// ===== NEW CODE START — currency format (agent A03, 2026-08-19) =====
        function renderMetric(data) {
            var pct = Number(data.percentage || 0);

            if ($valueEl) {
                $valueEl.text(pct + '%');
                $valueEl.attr('title', pct + '%');
            }
            if ($metaEl) {
                $metaEl.text(label("VAS_181_OfIssuedValueMTD", "Of issued value MTD"));
            }
            if ($card) { $card.prop('disabled', false); }
        }
// ===== NEW CODE END — currency format =====
// ----- OLD CODE (kept for rollback, do not delete) -----
        function renderMetric(data) {
            var pct = Number(data.percentage || 0);

            if ($valueEl) {
                $valueEl.text(pct + '%');
                $valueEl.attr('title', pct + '%');
            }
            if ($metaEl) {
                $metaEl.text(label("VAS_181_OfIssuedValueMTD", "Of issued value MTD"));
            }
            if ($card) { $card.prop('disabled', false); }
        }
// ----- END OLD CODE -----

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) { $metaEl.text(label("VAS_181_CouldntLoad", "Couldn't load")); }
            if ($card) { $card.prop('disabled', true); }
        }

        function openProductionIssuesList() {
            // Keep in lock-step with GetProductionIssueIdsData in the controller. The
            // TabWhereClause is a flat M_Inventory_ID IN (...) list, NOT a correlated
            // EXISTS(SELECT 1 FROM M_InventoryLine ...) subquery - the host window's
            // own "duplicate DocumentNo" grid diagnostic does naive, parenthesis-
            // unaware text surgery on the TabWhereClause looking for a FROM to lift
            // out, and it mishandled the nested EXISTS(...) (confirmed via the app
            // log: it produced malformed SQL and Oracle rejected it with ORA-00933,
            // which is what was actually hanging this drill-through). A flat ID list
            // has no FROM/subquery in it at all, so there is nothing for that
            // diagnostic query to mishandle.
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_181_ProductionIssuesWidget/GetProductionIssueIds',
                type: 'GET',
                cache: false,
                success: function (res) {
                    var data = parseResponse(res);
                    if (data.error) { return; }
                    var ids = data.ids || [];
                    var idList = ids.length ? ids.join(',') : '-1';
                    var where = "M_Inventory.M_Inventory_ID IN (" + idList + ")";
                    $self.widgetFirevalueChanged({
                        "TabWhereClause": where,
                        "TabLayout": "N",
                        "TabIndex": "0"
                    });
                }
            });
        }

        function createWidget() {
            var title = label("VAS_181_ProductionIssues", "Production Issues");
            $card = $(
                '<button type="button" class="vas-piw-card vas-widget-bg" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-piw-label">' + escapeHtml(title) + '</div>' +
                '<div class="vas-piw-value">—</div>' +
                '<div class="vas-piw-meta"></div>' +
                '</button>'
            );

            $valueEl = $card.find('.vas-piw-value');
            $metaEl = $card.find('.vas-piw-meta');

            $card.on('click', function () { openProductionIssuesList(); });
            $root.append($card);

            $busy = $('<div class="vas-piw-busy vas-piw-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

    VAS.VAS_181_ProductionIssuesWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_181_ProductionIssuesWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_181_ProductionIssuesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_181_ProductionIssuesWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_181_ProductionIssuesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_181_ProductionIssuesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
