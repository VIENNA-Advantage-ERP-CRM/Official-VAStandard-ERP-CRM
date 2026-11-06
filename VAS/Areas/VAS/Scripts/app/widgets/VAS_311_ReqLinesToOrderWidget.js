/**
 * VAS_311_ReqLinesToOrderWidget - 6x2 grid on the Requisition screen (and its Home copy).
 * Completed-requisition lines with quantity on no PO and no Material Transfer, earliest need-by
 * first: requisition, product, qty to order, amount, need-by, suggested vendor (current vendor of
 * the product) and a due chip. Rows that do not fit are paged; a row opens the requisition popup.
 * Backend: VAS_311_ReqLinesToOrderWidget/GetLines
 *
 * Labels / Message Keys
 *  VAS_311_LinesToOrder   Lines to Order
 *  VAS_311_Subtitle       Open lines with no PO or transfer · earliest need-by first
 *  VAS_311_ToOrder        to order
 *  VAS_311_Requisition    Requisition
 *  VAS_311_Product        Product
 *  VAS_311_Qty            Qty
 *  VAS_311_Amount         Amount
 *  VAS_311_NeedBy         Need by
 *  VAS_311_Vendor         Suggested vendor
 *  VAS_311_Status         Status
 *  VAS_311_NoVendor       No vendor
 *  VAS_311_Lines          lines
 *  VAS_311_Empty          Every completed requisition line is on a PO or transfer.
 */
; (function (VAS, $) {
    "use strict";
    var U = VAS.RequisitionWidgetUtil;

    VAS.VAS_311_ReqLinesToOrderWidget = function () {
        var self = this;
        var $root = $('<div class="vas-rqw-root">');
        var $card, $tb, $help, $pager, data = null, state = { page: 0 }, stopResize = null;
        this.frame = null;
        this.windowNo = -1;

        this.Initalize = function () {
            $card = $('<div class="vas-rqw-card">' +
                U.header(U.icon.cart, U.lbl('VAS_311_LinesToOrder', 'Lines to Order'),
                    U.lbl('VAS_311_Subtitle', 'Open lines with no PO or transfer · earliest need-by first'), 'amt') +
                '<div class="vas-rqw-tb" style="--vas-rqw-cols:minmax(0,1fr) minmax(0,2fr) minmax(5.6em,1fr) 5.6em 4.4em minmax(0,1.4fr) 5.8em"></div>' +
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
            U.get('VAS_311_ReqLinesToOrderWidget/GetLines').done(function (d) {
                data = d;
                $card.find('[data-pill="amt"]').text(U.money(d.amount, d.currency) + ' ' + U.lbl('VAS_311_ToOrder', 'to order'));
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
            var cur = data.currency;
            U.renderRows({
                $tb: $tb, $help: $help, $pager: $pager, state: state, rows: data.rows,
                emptyText: U.lbl('VAS_311_Empty', 'Every completed requisition line is on a PO or transfer.'),
                head: U.gridHead([U.lbl('VAS_311_Requisition', 'Requisition'), U.lbl('VAS_311_Product', 'Product'), U.lbl('VAS_311_Qty', 'Qty'),
                    U.lbl('VAS_311_Amount', 'Amount') + (cur && cur.Symbol ? ' ' + cur.Symbol : ''), U.lbl('VAS_311_NeedBy', 'Need by'),
                    U.lbl('VAS_311_Vendor', 'Suggested vendor'), U.lbl('VAS_311_Status', 'Status')], [2, 3, 4]),
                cell: function (r) {
                    return '<span class="b">' + U.esc(r.documentNo) + '</span>' +
                        '<span title="' + U.esc(r.product + (r.productCode ? ' (' + r.productCode + ')' : '')) + '">' + U.esc(r.product) + '</span>' +
                        '<span class="vas-rqw-r" title="' + U.esc(U.num(r.qty) + ' ' + (r.uom || '')) + '">' + U.num(r.qty) + ' ' + U.esc(r.uom || '') + '</span>' +
                        '<span class="vas-rqw-r">' + U.amount(r.amount, cur) + '</span>' +
                        '<span class="vas-rqw-r">' + U.date(r.needBy) + '</span>' +
                        (r.vendor ? '<span class="m" title="' + U.esc(r.vendor) + '">' + U.esc(r.vendor) + '</span>'
                            : '<span>' + U.chip('n', U.lbl('VAS_311_NoVendor', 'No vendor')) + '</span>') +
                        '<span>' + U.needChip(r.needBy) + '</span>';
                },
                helpText: function (a, b, n) {
                    return U.lbl('VAS_305_Showing', 'Showing') + ' ' + a + '–' + b + ' ' + U.lbl('VAS_305_Of', 'of') + ' ' + n + ' ' + U.unit(n, 'VAS_311_Lines', 'lines');
                },
                onClick: function (r) { U.showRequisition(self, r.requisitionId); }
            });
        };

        this.getRoot = function () { return $root; };
        this.disposeComponent = function () { if (stopResize) { stopResize(); } $root.remove(); };
    };

    U.makeWidget(VAS.VAS_311_ReqLinesToOrderWidget);
})(VAS, jQuery);
