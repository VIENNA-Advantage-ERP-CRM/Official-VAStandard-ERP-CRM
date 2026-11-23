/**
 * VAS_311_ReqLinesToOrderWidget - 6x2 grid on the Requisition screen (and its Home copy).
 * Completed-requisition lines with quantity on no PO and no Material Transfer, earliest need-by
 * first: requisition, product, qty to order, amount (price-list currency), need-by and a due chip
 * (VAS163 2026-11-20: Sr 37 - suggested vendor column removed). Rows that do not fit are paged;
 * a row opens the requisition popup.
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
                // VAS163 2026-11-20: Sr 37 - vendor column removed; wider amount (currency sign) and date (year) columns
                '<div class="vas-rqw-tb" style="--vas-rqw-cols:minmax(0,1fr) minmax(0,2fr) minmax(5.6em,1fr) minmax(7em,1.2fr) 6.6em 8.2em"></div>' +
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
            // VAS163 2026-11-20: Sr 37 - 'Suggested vendor' column removed; Sr 40 - the Amount heading carries no currency
            // sign (each row shows its own); Sr 41 - row amount in the requisition's price-list currency with its sign;
            // Sr 36 - date with the year; every cell has a hover title.
            U.renderRows({
                $tb: $tb, $help: $help, $pager: $pager, state: state, rows: data.rows,
                emptyText: U.lbl('VAS_311_Empty', 'Every completed requisition line is on a PO or transfer.'),
                head: U.gridHead([U.lbl('VAS_311_Requisition', 'Requisition'), U.lbl('VAS_311_Product', 'Product'), U.lbl('VAS_311_Qty', 'Qty'),
                    U.lbl('VAS_311_Amount', 'Amount'), U.lbl('VAS_311_NeedBy', 'Need by'), U.lbl('VAS_311_Status', 'Status')], [2, 3, 4]),
                cell: function (r) {
                    var qty = U.num(r.qty) + ' ' + (r.uom || '');
                    var amt = U.rowMoney(r, cur);
                    var need = U.date(r.needBy);
                    var chip = U.needChip(r.needBy);
                    return '<span class="b" title="' + U.esc(r.documentNo) + '">' + U.esc(r.documentNo) + '</span>' +
                        '<span title="' + U.esc(r.product + (r.productCode ? ' (' + r.productCode + ')' : '')) + '">' + U.esc(r.product) + '</span>' +
                        '<span class="vas-rqw-r" title="' + U.esc(qty) + '">' + U.esc(qty) + '</span>' +
                        '<span class="vas-rqw-r" title="' + U.esc(amt) + '">' + U.esc(amt) + '</span>' +
                        '<span class="vas-rqw-r" title="' + U.esc(need) + '">' + U.esc(need) + '</span>' +
                        '<span title="' + U.esc($('<div>').html(chip).text()) + '">' + chip + '</span>';
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
