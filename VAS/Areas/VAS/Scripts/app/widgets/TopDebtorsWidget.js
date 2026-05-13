/**
 * Top Debtors Widget
 * Purpose - Show customers with the largest unpaid balances on home/finance dashboard
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                              | Message Key                    | MsgText
 * ----+-----------------------------------------------------------+--------------------------------+-----------------------------------------------------------
 *  1  | Top Debtors                                               | VIS_TopDebtors                 | Top Debtors
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
        var $root = $('<div style="height:100%;width:100%;font-family:Roboto,sans-serif;">');

        var $listBody;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            loadData();
        };

        /* ── Load data from backend ── */
        function loadData() {
            $.ajax({
                url: VIS.Application.contextUrl + 'TopDebtors/GetTopDebtors',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (Array.isArray(data)) {
                        renderRows(data);
                    }
                },
                error: function () { /* leave loading placeholder on error */ }
            });
        }

        /* ── Format currency ── */
        function formatCurrency(value) {
            if (value >= 1000000) {
                return (value / 1000000).toFixed(1).replace(/\.0$/, '') + 'M';
            }
            if (value >= 1000) {
                return (value / 1000).toFixed(0) + ',' + (value % 1000 < 100 ? (value % 1000 < 10 ? '00' : '0') : '') + (value % 1000);
            }
            return Math.round(value).toLocaleString('en-US');
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

        /* ── Avatar background colors (cycled by index) ── */
        var AVATAR_COLORS = [
            { bg: '#DBEAFE', color: '#1E40AF' },
            { bg: '#FCE7F3', color: '#9D174D' },
            { bg: '#D1FAE5', color: '#065F46' },
            { bg: '#FEF3C7', color: '#92400E' },
            { bg: '#EDE9FE', color: '#5B21B6' },
            { bg: '#FFE4E6', color: '#9F1239' }
        ];

        /* ── Risk chip ── */
        function riskChip(daysOverdue) {
            var isHighRisk = daysOverdue > 0;
            var bg    = isHighRisk ? '#FAD7D7' : '#CCEFDD';
            var color = isHighRisk ? '#8F2D2D' : '#0C5D38';
            var label = isHighRisk ? lbl("VIS_HighRisk", 'HIGH RISK') : lbl("VIS_OnTrack", 'ON TRACK');
            return '<span style="' +
                'display:inline-block;padding:3px 10px;border-radius:999px;' +
                'font-size:10px;font-weight:700;white-space:nowrap;letter-spacing:0.4px;' +
                'background:' + bg + ';color:' + color + ';' +
            '">' + label + '</span>';
        }

        /* ── Overdue label ── */
        function overdueLabel(statusText) {
            return '<span style="font-size:12px;color:#748494;">' + (statusText || '') + '</span>';
        }

        /* ── Render rows ── */
        function renderRows(rows) {
            if (!$listBody) return;
            $listBody.empty();

            if (!rows || rows.length === 0) {
                $listBody.append(
                    '<div style="text-align:center;padding:24px 16px;color:#748494;font-size:12px;">' +
                        lbl("VIS_NoData", 'No data') +
                    '</div>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var r = rows[i];
                var isLast = (i === rows.length - 1);
                var ac = AVATAR_COLORS[i % AVATAR_COLORS.length];
                var initials = avatarInitials(r.customerName);

                var $row = $(
                    '<div style="' +
                        'display:flex;align-items:center;gap:12px;' +
                        'padding:12px 16px;' +
                        (isLast ? '' : 'border-bottom:1px solid #EDF2F6;') +
                        'transition:background 0.15s;cursor:pointer;' +
                    '">' +
                        /* Avatar */
                        '<div style="' +
                            'width:36px;height:36px;border-radius:50%;flex-shrink:0;' +
                            'background:' + ac.bg + ';color:' + ac.color + ';' +
                            'display:flex;align-items:center;justify-content:center;' +
                            'font-size:12px;font-weight:700;letter-spacing:0.3px;' +
                        '">' + initials + '</div>' +
                        /* Name + overdue */
                        '<div style="flex:1;min-width:0;">' +
                            '<div style="font-size:14px;font-weight:700;color:#102C3F;' +
                                'white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">' +
                                (r.customerName || '—') +
                            '</div>' +
                            '<div style="margin-top:2px;">' + overdueLabel(r.statusText) + '</div>' +
                        '</div>' +
                        /* Amount + chip */
                        '<div style="display:flex;flex-direction:column;align-items:flex-end;gap:4px;flex-shrink:0;">' +
                            '<span style="font-size:15px;font-weight:700;color:#102C3F;font-variant-numeric:tabular-nums;">' +
                                formatCurrency(r.unpaidBalance) +
                            '</span>' +
                            riskChip(r.daysOverdue) +
                        '</div>' +
                    '</div>'
                );

                $row.on('mouseenter', function () { $(this).css('background', '#F8FBFF'); });
                $row.on('mouseleave', function () { $(this).css('background', 'transparent'); });

                $listBody.append($row);
            }
        }

        /* ── Build DOM ── */
        function createWidget() {
            var $card = $(
                '<div style="' +
                    'background:linear-gradient(180deg,rgba(255,255,255,0.82) 0%,rgba(255,255,255,0.58) 100%);' +
                    'border:2px solid #fff;' +
                    'border-radius:14px;' +
                    'box-shadow:0 10px 24px rgba(15,61,97,0.06);' +
                    'overflow:hidden;' +
                    'height:100%;' +
                    'box-sizing:border-box;' +
                    'display:flex;flex-direction:column;' +
                '">'
            );

            /* ── Header ── */
            var $header = $(
                '<div style="display:flex;align-items:center;justify-content:space-between;padding:16px 18px 12px;">' +
                    /* Left: icon + title */
                    '<div style="display:flex;align-items:center;gap:10px;">' +
                        '<div style="' +
                            'width:36px;height:36px;border-radius:8px;flex-shrink:0;' +
                            'background:#EAF8FF;' +
                            'display:flex;align-items:center;justify-content:center;' +
                        '">' +
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
                            '<div style="font-size:14px;font-weight:700;color:#102C3F;line-height:1.2;">' +
                                lbl("VIS_TopDebtors", 'Top Debtors') +
                            '</div>' +
                            '<div style="font-size:10px;color:#748494;letter-spacing:0.6px;text-transform:uppercase;margin-top:2px;">' +
                                lbl("VIS_LargestUnpaidBalances", 'LARGEST UNPAID BALANCES') +
                            '</div>' +
                        '</div>' +
                    '</div>' +
                    /* Right: Chase all link */
                    '<div style="display:flex;align-items:center;gap:4px;cursor:pointer;" id="vis-topdebtors-chaseall-' + ($self.AD_UserHomeWidgetID || '') + '">' +
                        '<span style="font-size:13px;font-weight:600;color:#0083DA;">' +
                            lbl("VIS_ChaseAll", 'Chase all') +
                        '</span>' +
                        '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" ' +
                            'stroke="#0083DA" stroke-width="2.2" ' +
                            'stroke-linecap="round" stroke-linejoin="round">' +
                            '<line x1="7" y1="17" x2="17" y2="7"/>' +
                            '<polyline points="7 7 17 7 17 17"/>' +
                        '</svg>' +
                    '</div>' +
                '</div>'
            );

            /* ── Scrollable list body ── */
            $listBody = $(
                '<div style="flex:1;overflow-y:auto;padding-bottom:4px;">' +
                    '<div style="text-align:center;padding:24px 16px;color:#748494;font-size:12px;">' +
                        lbl("VIS_Loading", 'Loading…') +
                    '</div>' +
                '</div>'
            );

            $card.append($header).append($listBody);
            $root.append($card);
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {
            if ($listBody) {
                $listBody.html(
                    '<div style="text-align:center;padding:24px 16px;color:#748494;font-size:12px;">' +
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

    VIS.TopDebtorsWidget.prototype.refreshWidget = function () {};

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
