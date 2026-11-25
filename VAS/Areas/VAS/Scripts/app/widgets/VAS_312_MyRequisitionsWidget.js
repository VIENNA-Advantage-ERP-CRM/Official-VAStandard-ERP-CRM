/**
 * VAS_312_MyRequisitionsWidget - 3x2 grid on the Requisition screen (and its Home copy).
 * Requisitions created by the logged-in user in the month / year chosen in the title row (every
 * document status, VAS163 2026-11-20: Sr 45 / Sr 46), newest first, with need-by and the live status.
 * A row opens the popup.
 * Backend: VAS_312_MyRequisitionsWidget/GetMyRequisitions?year=&month=
 *
 * Labels / Message Keys
 *  VAS_278_Month           Month  (reused, aria label of the month filter)
 *  VAS_278_Year            Year   (reused, aria label of the year filter)
 *  VAS_312_MyRequisitions  My Requisitions
 *  VAS_312_Subtitle        Raised by me · newest first
 *  VAS_312_Open            open
 *  VAS_312_Requisition     Requisition
 *  VAS_312_NeedBy          Need by
 *  VAS_312_Status          Status
 *  VAS_312_Empty           You have not raised a requisition in the last 12 months.
 */
; (function (VAS, $) {
    "use strict";
    var U = VAS.RequisitionWidgetUtil;

    VAS.VAS_312_MyRequisitionsWidget = function () {
        var self = this;
        var $root = $('<div class="vas-rqw-root">');
        var $card, $tb, $help, $pager, data = null, state = { page: 0 }, stopResize = null;
        var $monthSel = null, $yearSel = null, req = 0; // VAS163 2026-11-20: Sr 45
        this.frame = null;
        this.windowNo = -1;

        /** VAS163 2026-11-20: Sr 45 - Month / Year filter in the title row (same pattern as VAS_278), browser-culture month names. */
        function buildFilter() {
            var now = new Date(), lang = window.navigator.language || 'en-US';
            var months = '', years = '';
            for (var i = 0; i < 12; i++) {
                months += '<option value="' + (i + 1) + '"' + (i === now.getMonth() ? ' selected' : '') + '>' +
                    U.esc(new Date(2000, i, 1).toLocaleDateString(lang, { month: 'short' })) + '</option>';
            }
            for (var y = now.getFullYear() - 4; y <= now.getFullYear() + 1; y++) {
                years += '<option value="' + y + '"' + (y === now.getFullYear() ? ' selected' : '') + '>' + y + '</option>';
            }
            $monthSel = $('<select class="vas-rqw-msel" aria-label="' + U.esc(U.lbl('VAS_278_Month', 'Month')) + '">' + months + '</select>');
            $yearSel = $('<select class="vas-rqw-msel" aria-label="' + U.esc(U.lbl('VAS_278_Year', 'Year')) + '">' + years + '</select>');
            $monthSel.add($yearSel).on('click', function (e) { e.stopPropagation(); }).on('change', function () { self.reload(); });
            return $('<div class="vas-rqw-mf"></div>').append($monthSel, $yearSel);
        }

        this.Initalize = function () {
            $card = $('<div class="vas-rqw-card">' +
                U.header(U.icon.user, U.lbl('VAS_312_MyRequisitions', 'My Requisitions'), U.lbl('VAS_312_Subtitle', 'Raised by me · newest first'), 'open') +
                // VAS163 2026-11-20: Sr 43 - wider date column (the date carries the year)
                '<div class="vas-rqw-tb" style="--vas-rqw-cols:minmax(0,1fr) 6.6em minmax(0,1.3fr)"></div>' +
                '<div class="vas-rqw-ft"><span class="vas-rqw-hp"></span><span class="vas-rqw-pg"></span></div>' +
                '<span class="vas-rqw-busy"><i></i></span></div>');
            $root.empty().append($card);
            // VAS163 2026-11-25: Sr 80 the "N open" badge is removed; the month / year filter takes its place at the right.
            var $openPill = $card.find('[data-pill="open"]');
            $openPill.before(buildFilter());
            $openPill.remove();
            $tb = $card.find('.vas-rqw-tb'); $help = $card.find('.vas-rqw-hp'); $pager = $card.find('.vas-rqw-pg');
            stopResize = U.onResize($root[0], function () { self.relayout(); });
            self.reload();
        };

        this.reload = function () {
            if (!$card) { return; }
            var mine = ++req;
            $card.find('.vas-rqw-busy').addClass('on');
            // VAS163 2026-11-20: Sr 45 - requisitions of the selected month / year
            U.get('VAS_312_MyRequisitionsWidget/GetMyRequisitions', { year: Number($yearSel.val()), month: Number($monthSel.val()) }).done(function (d) {
                if (mine !== req) { return; }
                data = d; // VAS163 2026-11-25: Sr 80 no "N open" badge any more
                state.page = 0;
                self.relayout();
            }).fail(function (err) {
                if (mine !== req) { return; }
                data = null;
                $tb.html('<div class="vas-rqw-msg err">' + U.esc(err) + '</div>');
                $help.text(''); $pager.empty();
            }).always(function () { if (mine === req) { $card.find('.vas-rqw-busy').removeClass('on'); } });
        };

        this.relayout = function () {
            if (!data || !$tb) { return; }
            U.renderRows({
                $tb: $tb, $help: $help, $pager: $pager, state: state, rows: data.rows,
                emptyText: U.lbl('VAS_312_Empty', 'You have not raised a requisition in the last 12 months.'),
                head: U.gridHead([U.lbl('VAS_312_Requisition', 'Requisition'), U.lbl('VAS_312_NeedBy', 'Need by'), U.lbl('VAS_312_Status', 'Status')], [1]),
                cell: function (r) {
                    // VAS163 2026-11-20: Sr 43 - date with the year; Sr 44 - Closed / Voided come as separate statuses
                    var need = U.date(r.needBy);
                    var chip = r.isLate ? U.needChip(r.needBy) : U.statusChip(r.status, r.statusName);
                    return '<span class="b" title="' + U.esc(r.documentNo + ' · ' + U.money(r.amount, data.currency)) + '">' + U.esc(r.documentNo) + '</span>' +
                        '<span class="vas-rqw-r" title="' + U.esc(need) + '">' + U.esc(need) + '</span>' +
                        '<span title="' + U.esc($('<div>').html(chip).text()) + '">' + chip + '</span>';
                },
                onClick: function (r) { U.showRequisition(self, r.requisitionId); },
                helpText: function (a, b, n) {
                    return U.lbl('VAS_305_Showing', 'Showing') + ' ' + a + '–' + b + ' ' + U.lbl('VAS_305_Of', 'of') + ' ' + n + ' ' + U.unit(n, 'VAS_314_Requisitions', 'requisitions');
                }
            });
        };

        this.getRoot = function () { return $root; };
        this.disposeComponent = function () { if (stopResize) { stopResize(); } $root.remove(); };
    };

    U.makeWidget(VAS.VAS_312_MyRequisitionsWidget);
})(VAS, jQuery);
