/**
 * Top Debtors Widget
 * Purpose - Show customers with the largest unpaid balances on home/finance dashboard
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                              | Message Key                    | MsgText
 * ----+-----------------------------------------------------------+--------------------------------+-----------------------------------------------------------
 *  1  | Top Debtors                                               | VAS_061_TopDebtors             | Highest Debtors
 *  2  | LARGEST UNPAID BALANCES                                   | VAS_061_LargestUnpaidBalances  | LARGEST UNPAID BALANCES
 *  3  | Chase all                                                 | VAS_061_ChaseAll               | Chase all
 *  4  | days overdue                                              | VAS_061_DaysOverdue            | days overdue
 *  5  | Not yet overdue                                           | VAS_061_NotYetOverdue          | Not yet overdue
 *  6  | HIGH RISK                                                 | VAS_061_HighRisk               | HIGH RISK
 *  7  | ON TRACK                                                  | VAS_061_OnTrack                | ON TRACK
 *  8  | No data                                                   | VAS_061_NoData                 | No data
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    /* design.md §Widget Header / §Measurement Setup: keep --dash-inline-size on
       :root equal to the dashboard container's current pixel width so the title
       clamp resolves against the dashboard's visible content area, not the
       viewport. A single document-level ResizeObserver serves every widget (the
       var is global); without a marked container — or without ResizeObserver —
       the CSS falls back to 100vw. */
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

    VIS.TopDebtorsWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-td-root">');

        var $listBody;
        /* Base-currency symbol from the backend; rendered before each amount. */
        var currencySymbol = '';
        /* Base-currency ISO code (drives Indian vs international abbreviation) and precision. */
        var currencyIso = '';
        var currencyPrecision = null;
        /* Busy/loading overlay shown while data is being fetched (initial load + refresh). */
        var $busy;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        /* Escape text taken from the database before it is injected as HTML. */
        function escapeHtml(s) {
            if (s == null) return '';
            return String(s).replace(/[&<>"']/g, function (c) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
            });
        }

        /* Toggle the busy/loading overlay. */
        function showBusy(show) {
            if (!$busy || !$busy[0]) { return; }
            $busy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            loadData();
        };

        /* ── Load data from backend ── */
        function loadData() {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + 'TopDebtors/GetTopDebtors',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (data && typeof data === 'object') {
                        /* Backend returns { symbol, isoCode, stdPrecision, rows[] }; tolerate a bare array too. */
                        currencySymbol = data.symbol || '';
                        currencyIso = data.isoCode || '';
                        currencyPrecision = (data.stdPrecision === undefined || data.stdPrecision === null) ? null : data.stdPrecision;
                        renderRows(Array.isArray(data) ? data : (data.rows || []));
                    }
                },
                error: function () { /* leave loading placeholder on error */ },
                complete: function () { showBusy(false); }
            });
        }

        /* Build amount markup with the base-currency symbol placed *before* the amount;
           the minus sign (if any) precedes the symbol (e.g. -$1.2M). The compact
           magnitude (Indian vs international numbering by base currency, kept to the
           currency precision) comes from VIS.Util.formatCompactAmount. */
        function formatMetric(value, symbol) {
            value = Number(value || 0);
            var sign = value < 0 ? '-' : '';
            var symHtml = symbol ? '<span class="vas-td-cur">' + escapeHtml(symbol) + '</span>' : '';
            return sign + symHtml + VIS.Util.formatCompactAmount(value, currencyIso, currencyPrecision);
        }

        /* ── Avatar initials ── */
        function avatarInitials(name) {
            if (!name) return '??';
            var parts = name.trim().split(/\s+/);
            if (parts.length >= 2) {
                return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
            }
            return name.substring(0, 2).toUpperCase();
        }

        /* ── Risk chip (avatar palette + chip colours live in CSS) ── */
        function riskChip(daysOverdue) {
            var isHighRisk = daysOverdue > 0;
            var cls   = isHighRisk ? 'vas-td-chip-risk' : 'vas-td-chip-ontrack';
            var label = isHighRisk ? lbl("VAS_061_HighRisk", 'HIGH RISK') : lbl("VAS_061_OnTrack", 'ON TRACK');
            return '<span class="vas-td-chip ' + cls + '">' + label + '</span>';
        }

        /* ── Overdue label ── */
        function overdueLabel(statusText) {
            return '<span class="vas-td-overdue-label">' + escapeHtml(statusText || '') + '</span>';
        }

        /* ── Render rows ── */
        function renderRows(rows) {
            if (!$listBody) return;
            $listBody.empty();

            if (!rows || rows.length === 0) {
                $listBody.append(
                    '<div class="vas-td-nodata">' +
                        lbl("VAS_061_NoData", 'No data') +
                    '</div>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var r = rows[i];
                var initials = avatarInitials(r.customerName);

                /* Row divider + hover are handled in CSS; the palette index cycles 0–5. */
                $listBody.append(
                    '<div class="vas-td-list-row">' +
                        /* Avatar */
                        '<div class="vas-td-avatar vas-td-avatar-' + (i % 6) + '">' + escapeHtml(initials) + '</div>' +
                        /* Name + overdue */
                        '<div class="vas-td-name-wrap">' +
                            '<div class="vas-td-name">' + escapeHtml(r.customerName || '—') + '</div>' +
                            '<div class="vas-td-overdue-wrap">' + overdueLabel(r.statusText) + '</div>' +
                        '</div>' +
                        /* Amount + risk chip (base-currency symbol before the amount) */
                        '<div class="vas-td-amount-wrap">' +
                            '<span class="vas-td-amount">' + formatMetric(r.unpaidBalance, currencySymbol) + '</span>' +
                            riskChip(r.daysOverdue) +
                        '</div>' +
                    '</div>'
                );
            }
        }

        /* ── Build DOM ── */
        function createWidget() {
            var $card = $(
                '<div class="vas-td-card">'
            );

            /* ── Header ── */
            var $header = $(
                '<div class="vas-td-header">' +
                    /* Left: icon + title */
                    '<div class="vas-td-header-left">' +
                        '<div class="vas-td-icon">' +
                            '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" ' +
                                'stroke="#0083DA" stroke-width="1.8" ' +
                                'stroke-linecap="round" stroke-linejoin="round">' +
                                '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>' +
                                '<circle cx="9" cy="7" r="4"/>' +
                                '<path d="M23 21v-2a4 4 0 0 0-3-3.87"/>' +
                                '<path d="M16 3.13a4 4 0 0 1 0 7.75"/>' +
                            '</svg>' +
                        '</div>' +
                        '<div>' +
                            '<div class="vas-td-title">' +
                                lbl("VAS_061_TopDebtors", 'Highest Debtors') +
                            '</div>' +
                            '<div class="vas-td-subtitle">' +
                                lbl("VAS_061_LargestUnpaidBalances", 'LARGEST UNPAID BALANCES') +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            /* ── Scrollable list body (empty until data loads; the busy overlay covers the wait) ── */
            $listBody = $('<div class="vas-td-list-body">');

            $card.append($header).append($listBody);
            $root.append($card);

            /* Busy/loading overlay over the whole card, using the core spinner classes. Hidden until
               a fetch is in flight; shown for both initial load and refresh. */
            $busy = $('<div class="vas-td-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $busy[0].style.visibility = 'hidden';
            $root.append($busy);
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {
            loadData();
        };

        /* ── Root accessor ── */
        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VIS.TopDebtorsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VIS.TopDebtorsWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());

        /* Self-wire the dashboard-width CSS variable the title clamp reads. */
        ensureDashInlineSizeVar(this.getRoot());
    };

    VIS.TopDebtorsWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.TopDebtorsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
