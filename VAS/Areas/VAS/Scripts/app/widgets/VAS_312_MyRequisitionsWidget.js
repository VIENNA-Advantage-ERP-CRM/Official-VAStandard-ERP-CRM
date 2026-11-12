/**
 * VAS_312_MyRequisitionsWidget - 3x2 grid on the Requisition screen (and its Home copy).
 * Requisitions of the logged-in user (requester, or creator when no requester is set) of the
 * last 12 months, newest first, with need-by and the live status. A row opens the popup.
 * Backend: VAS_312_MyRequisitionsWidget/GetMyRequisitions
 *
 * Labels / Message Keys
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
        this.frame = null;
        this.windowNo = -1;

        this.Initalize = function () {
            $card = $('<div class="vas-rqw-card">' +
                U.header(U.icon.user, U.lbl('VAS_312_MyRequisitions', 'My Requisitions'), U.lbl('VAS_312_Subtitle', 'Raised by me · newest first'), 'open') +
                '<div class="vas-rqw-tb" style="--vas-rqw-cols:minmax(0,1fr) 4.6em minmax(0,1.3fr)"></div>' +
                '<div class="vas-rqw-ft"><span class="vas-rqw-hp"></span><span class="vas-rqw-pg"></span></div>' +
                '<span class="vas-rqw-busy"><i></i></span></div>');
            $root.empty().append($card);
            $tb = $card.find('.vas-rqw-tb'); $help = $card.find('.vas-rqw-hp'); $pager = $card.find('.vas-rqw-pg');
            stopResize = U.onResize($root[0], function () { self.relayout(); });
            self.reload();
        };

        this.reload = function () {
            if (!$card) { return; }
            $card.find('.vas-rqw-busy').addClass('on');
            U.get('VAS_312_MyRequisitionsWidget/GetMyRequisitions').done(function (d) {
                data = d;
                $card.find('[data-pill="open"]').text(U.num(d.openCount) + ' ' + U.lbl('VAS_312_Open', 'open'));
                state.page = 0;
                self.relayout();
            }).fail(function (err) {
                data = null;
                $tb.html('<div class="vas-rqw-msg err">' + U.esc(err) + '</div>');
                $help.text(''); $pager.empty();
            }).always(function () { $card.find('.vas-rqw-busy').removeClass('on'); });
        };

        this.relayout = function () {
            if (!data || !$tb) { return; }
            U.renderRows({
                $tb: $tb, $help: $help, $pager: $pager, state: state, rows: data.rows,
                emptyText: U.lbl('VAS_312_Empty', 'You have not raised a requisition in the last 12 months.'),
                head: U.gridHead([U.lbl('VAS_312_Requisition', 'Requisition'), U.lbl('VAS_312_NeedBy', 'Need by'), U.lbl('VAS_312_Status', 'Status')], [1]),
                cell: function (r) {
                    return '<span class="b" title="' + U.esc(U.money(r.amount, data.currency)) + '">' + U.esc(r.documentNo) + '</span>' +
                        '<span class="vas-rqw-r">' + U.date(r.needBy) + '</span>' +
                        '<span>' + (r.isLate ? U.needChip(r.needBy) : U.statusChip(r.status, r.statusName)) + '</span>';
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
