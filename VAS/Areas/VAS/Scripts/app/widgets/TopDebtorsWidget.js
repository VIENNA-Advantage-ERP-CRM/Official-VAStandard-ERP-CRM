/**
 * Top Debtors Widget
 * Purpose - Show customers with the largest unpaid balances on home/finance dashboard
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                              | Message Key                    | MsgText
 * ----+-----------------------------------------------------------+--------------------------------+-----------------------------------------------------------
 *  1  | Top Debtors                                               | VIS_TopDebtors                 | Highest Debtors
 *  2  | LARGEST UNPAID BALANCES                                   | VIS_LargestUnpaidBalances      | LARGEST UNPAID BALANCES
 *  3  | Chase all                                                 | VIS_ChaseAll                   | Chase all
 *  4  | days overdue                                              | VIS_DaysOverdue                | days overdue
 *  5  | Not yet overdue                                           | VIS_NotYetOverdue              | Not yet overdue
 *  6  | HIGH RISK                                                 | VIS_HighRisk                   | HIGH RISK
 *  7  | ON TRACK                                                  | VIS_OnTrack                    | ON TRACK
 *  8  | Loading…                                                  | VIS_Loading                    | Loading…
 *  9  | No data                                                   | VIS_NoData                     | No data
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.TopDebtorsWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="vas-td-root">');

        var $listBody;
        /* Base-currency symbol from the backend; rendered before each amount. */
        var currencySymbol = '';
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
                        /* Backend returns { symbol, rows[] }; tolerate a bare array too. */
                        currencySymbol = data.symbol || '';
                        renderRows(Array.isArray(data) ? data : (data.rows || []));
                    }
                },
                error: function () { /* leave loading placeholder on error */ },
                complete: function () { showBusy(false); }
            });
        }

        /* ── Format the numeric part of an amount (k / M abbreviations) ── */
        function formatCurrency(value) {
            var stdPrecision = VIS.Env.getCtx().getStdPrecision();
            var absVal = Math.abs(Number(value) || 0);

            if (absVal >= 1000000) {
                return (absVal / 1000000).toFixed(1).replace(/\.0$/, '') + 'M';
            }
            if (absVal >= 1000) {
                return Math.round(absVal / 1000) + 'k';
            }
            return absVal.toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
        }

        /* Build amount markup with the base-currency symbol placed *before* the amount;
           the minus sign (if any) precedes the symbol (e.g. -$1.2M). */
        function formatMetric(value, symbol) {
            value = Number(value || 0);
            var sign = value < 0 ? '-' : '';
            var symHtml = symbol ? '<span class="vas-td-cur">' + escapeHtml(symbol) + '</span>' : '';
            return sign + symHtml + formatCurrency(value);
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
            var label = isHighRisk ? lbl("VIS_HighRisk", 'HIGH RISK') : lbl("VIS_OnTrack", 'ON TRACK');
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
                        lbl("VIS_NoData", 'No data') +
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
                                lbl("VIS_TopDebtors", 'Highest Debtors') +
                            '</div>' +
                            '<div class="vas-td-subtitle">' +
                                lbl("VIS_LargestUnpaidBalances", 'LARGEST UNPAID BALANCES') +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            /* ── Scrollable list body ── */
            $listBody = $(
                '<div class="vas-td-list-body">' +
                    '<div class="vas-td-nodata">' +
                        lbl("VIS_Loading", 'Loading…') +
                    '</div>' +
                '</div>'
            );

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
            if ($listBody) {
                $listBody.html(
                    '<div class="vas-td-nodata">' +
                        lbl("VIS_Loading", 'Loading…') +
                    '</div>'
                );
            }
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
    };

    VIS.TopDebtorsWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.TopDebtorsWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
