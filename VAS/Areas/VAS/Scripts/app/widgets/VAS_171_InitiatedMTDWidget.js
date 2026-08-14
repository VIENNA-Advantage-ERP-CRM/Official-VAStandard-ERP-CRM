/**
 * Transfer Initiated MTD KPI Widget (Material Transfer Dashboard)
 * Purpose - Read-only 2x1 glass KPI tile showing count of transfer documents
 *           initiated (created) in the current calendar month to date.
 *           Neutral tone always (volume is informational, not an alert).
 * Prefix  - VAS_171_
 *
 * Labels / Message Keys Table:
 *  #  | Fallback Text                                    | Message Key
 * ----+--------------------------------------------------+-----------------------------------
 *  1  | Transfer Initiated MTD                           | VAS_171_TransferInitiatedMTD
 *  2  | This month to date                               | VAS_171_ThisMonthToDate
 *  3  | Unable to load initiated transfers               | VAS_171_UnableToLoadData
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

    VAS.VAS_171_InitiatedMTDWidget = function () {

        this.frame;
        this.windowNo;

        var $self = this;
        var $wrapper = $('<div class="vas-initiated-mtd-container">');
        var $root = $('<div class="vas-initiated-mtd-root">');
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
            $busy.toggleClass('vas-initiated-mtd-hidden', !show);
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language);
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
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

        /* Build first-of-month and tomorrow date literals for the TabWhereClause.
           Mirrors the backend SQL predicate (Created >= month start AND Created < month end). */
        function monthStartIso() {
            var d = new Date();
            return d.getFullYear() + '-' +
                   String(d.getMonth() + 1).padStart(2, '0') + '-01';
        }

        function tomorrowIso() {
            var d = new Date();
            d.setDate(d.getDate() + 1);
            return d.getFullYear() + '-' +
                   String(d.getMonth() + 1).padStart(2, '0') + '-' +
                   String(d.getDate()).padStart(2, '0');
        }

        /* Drill-through to Material Transfer list filtered to transfers created this month.
           Excludes cancelled (DocStatus NOT IN ('VO','RE')) to match the backend count.
           Uses TabWhereClause so the list row count equals the KPI number. */
        function openTransferList() {
            var startDate = monthStartIso();
            var endDate = tomorrowIso();
            var where =
                "MMovement.IsActive = 'Y'" +
                " AND MMovement.DocStatus NOT IN ('VO', 'RE')" +
                " AND TRUNC(MMovement.Created) >= TO_DATE('" + startDate + "','YYYY-MM-DD')" +
                " AND TRUNC(MMovement.Created) < TO_DATE('" + endDate + "','YYYY-MM-DD')";

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
                url: VIS.Application.contextUrl + 'VAS_171_InitiatedMTDWidget/GetInitiatedMTDData',
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

        function renderMetric(data) {
            var count = Number(data.count || 0);

            if ($valueEl) {
                $valueEl.text(formatCount(count));
                $valueEl.attr('title', formatCount(count));
                $valueEl.attr('aria-live', 'polite');
            }

            if ($metaEl) {
                var metaMsg = lbl('VAS_171_ThisMonthToDate', 'This month to date');
                $metaEl.text(metaMsg);
                $metaEl.attr('title', metaMsg);
            }
        }

        function setError() {
            if ($valueEl) {
                $valueEl.text('—');
                $valueEl.removeAttr('title');
            }
            if ($metaEl) {
                var errText = lbl('VAS_171_UnableToLoadData', 'Unable to load initiated transfers');
                $metaEl.text(errText);
                $metaEl.attr('title', errText);
            }
        }

        function createWidget() {
            var title = lbl('VAS_171_TransferInitiatedMTD', 'Transfer Initiated MTD');
            var metaText = lbl('VAS_171_ThisMonthToDate', 'This month to date');

            var $card = $(
                '<button type="button" class="vas-initiated-mtd-card" aria-label="' + escapeHtml(title) + '">' +
                '<div class="vas-initiated-mtd-label">' + escapeHtml(title) + '</div>' +
                '<div class="vas-initiated-mtd-value">—</div>' +
                '<div class="vas-initiated-mtd-meta">' + escapeHtml(metaText) + '</div>' +
                '</button>'
            );

            $card.on('click', function () { openTransferList(); });

            $valueEl = $card.find('.vas-initiated-mtd-value');
            $metaEl = $card.find('.vas-initiated-mtd-meta');

            $root.append($card);

            $busy = $('<div class="vas-initiated-mtd-busy vas-initiated-mtd-hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
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

    VAS.VAS_171_InitiatedMTDWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener) { this.listener.widgetFirevalueChanged(value); }
    };

    VAS.VAS_171_InitiatedMTDWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_171_InitiatedMTDWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_171_InitiatedMTDWidget.prototype.widgetSizeChange = function (height, width) { };

    VAS.VAS_171_InitiatedMTDWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_171_InitiatedMTDWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
