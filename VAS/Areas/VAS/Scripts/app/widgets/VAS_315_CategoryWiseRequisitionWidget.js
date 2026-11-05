/**
 * VAS_315_CategoryWiseRequisitionWidget - 3x2 grid on the Requisition screen (and its Home copy).
 * Requested value of completed / closed requisitions of the last 12 months by product category,
 * largest first, with a share bar and the number of requisitions. Rows that do not fit are paged;
 * a row opens the category's lines, a line opens the requisition popup.
 * Backend: VAS_315_CategoryWiseRequisitionWidget/GetCategories
 *
 * Labels / Message Keys
 *  VAS_315_CategoryWise  Category wise Requisition
 *  VAS_315_Subtitle      Product categories · last 12 months by value
 *  VAS_315_Category      Category
 *  VAS_315_Share         Share
 *  VAS_315_Value         Value
 *  VAS_315_Reqs          Reqs
 *  VAS_315_Categories    categories
 *  VAS_315_LinesOn       lines on
 *  VAS_315_Charges       Charges        (server)
 *  VAS_315_Uncategorised Uncategorised  (server)
 *  VAS_315_Empty         No requisitions in the last 12 months.
 */
; (function (VAS, $) {
    "use strict";
    var U = VAS.RequisitionWidgetUtil;

    VAS.VAS_315_CategoryWiseRequisitionWidget = function () {
        var self = this;
        var $root = $('<div class="vas-rqw-root">');
        var $card, $tb, $help, $pager, data = null, state = { page: 0 }, stopResize = null;
        this.frame = null;
        this.windowNo = -1;

        this.Initalize = function () {
            $card = $('<div class="vas-rqw-card">' +
                U.header(U.icon.grid, U.lbl('VAS_315_CategoryWise', 'Category wise Requisition'), U.lbl('VAS_315_Subtitle', 'Product categories · last 12 months by value'), 'top') +
                '<div class="vas-rqw-tb" style="--vas-rqw-cols:minmax(0,1.5fr) minmax(0,1fr) 5em 2.8em"></div>' +
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
            U.get('VAS_315_CategoryWiseRequisitionWidget/GetCategories').done(function (d) {
                data = d;
                var c = d.categories || [];
                $card.find('[data-pill="top"]').text(c.length && d.total > 0 ? c[0].name + ' ' + Math.round(c[0].amount / d.total * 100) + '%' : '');
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
            // narrow widget: drop the top-category pill so the subtitle is not cut (the share is in the first row anyway)
            var $hd = $card.find('.vas-rqw-hd').removeClass('nopill'), st = $hd.find('.vas-rqw-st')[0];
            if (st && st.scrollWidth > st.clientWidth + 1) { $hd.addClass('nopill'); }
            var cats = data.categories || [], cur = data.currency;
            var max = cats.length ? cats[0].amount : 0;
            U.renderRows({
                $tb: $tb, $help: $help, $pager: $pager, state: state, rows: cats,
                emptyText: U.lbl('VAS_315_Empty', 'No requisitions in the last 12 months.'),
                head: U.gridHead([U.lbl('VAS_315_Category', 'Category'), U.lbl('VAS_315_Share', 'Share'),
                    U.lbl('VAS_315_Value', 'Value') + (cur && cur.Symbol ? ' ' + cur.Symbol : ''), U.lbl('VAS_315_Reqs', 'Reqs')], [2, 3]),
                cell: function (c) {
                    var pct = data.total > 0 ? Math.round(c.amount / data.total * 100) : 0;
                    var w = max > 0 ? (c.amount / max * 100) : 0;
                    return '<span class="b" title="' + U.esc(c.name) + '">' + U.esc(c.name) + '</span>' +
                        '<span title="' + pct + '%"><span class="vas-rqw-bar"><i style="width:' + w.toFixed(1) + '%"></i></span></span>' +
                        '<span class="vas-rqw-r" title="' + U.esc(U.amount(c.amount, cur)) + '">' + U.esc(U.money(c.amount, null)) + '</span>' +
                        '<span class="vas-rqw-r">' + U.num(c.requisitions) + '</span>';
                },
                helpText: function (a, b, n) {
                    return U.lbl('VAS_305_Showing', 'Showing') + ' ' + a + '–' + b + ' ' + U.lbl('VAS_305_Of', 'of') + ' ' + n + ' ' + U.unit(n, 'VAS_315_Categories', 'categories');
                },
                onClick: function (c) { openCategory(c); }
            });
        };

        function openCategory(c) {
            var cur = data.currency;
            var rows = (c.lines || []).map(function (l) {
                return '<div class="vas-rqw-gr" data-r="' + l.requisitionId + '"><span class="b">' + U.esc(l.documentNo) + '</span>' +
                    '<span title="' + U.esc(l.product) + '">' + U.esc(l.product) + '</span>' +
                    '<span class="vas-rqw-r">' + U.num(l.qty) + ' ' + U.esc(l.uom || '') + '</span>' +
                    '<span class="vas-rqw-r">' + U.date(l.needBy) + '</span>' +
                    '<span class="vas-rqw-r">' + U.amount(l.amount, cur) + '</span>' +
                    '<span>' + U.stateChip(l.state, l.needBy) + '</span></div>';
            });
            U.openModal({
                title: c.name,
                body: '<div class="vas-rqw-sh"><b>' + U.esc(c.name) + '</b><span>' + U.esc(U.num(c.lineCount) + ' ' + U.phrase(c.lineCount, 'VAS_315_LinesOn', 'lines on', function () { return U.lbl('VAS_305_Line1', 'line') + ' ·'; }) + ' ' +
                    U.num(c.requisitions) + ' ' + U.unit(c.requisitions, 'VAS_314_Requisitions', 'requisitions') + ' · ' + U.money(c.amount, cur)) + '</span></div>' +
                    U.table('minmax(0,1fr) minmax(0,1.8fr) 5.4em 4.6em 6em 7em', [2, 3, 4],
                        [U.lbl('VAS_305_Requisition', 'Requisition'), U.lbl('VAS_305_Product', 'Product'), U.lbl('VAS_305_Qty', 'Qty'),
                        U.lbl('VAS_305_NeedBy', 'Need by'), U.lbl('VAS_305_Amount', 'Amount') + (cur && cur.Symbol ? ' ' + cur.Symbol : ''), U.lbl('VAS_305_Status', 'Status')], rows),
                after: function ($m) {
                    $m.find('.vas-rqw-pb').off('click.rqwc').on('click.rqwc', '.vas-rqw-gr[data-r]', function () {
                        U.showRequisition(self, Number($(this).attr('data-r')));
                    });
                }
            });
        }

        this.getRoot = function () { return $root; };
        this.disposeComponent = function () { if (stopResize) { stopResize(); } $root.remove(); };
    };

    U.makeWidget(VAS.VAS_315_CategoryWiseRequisitionWidget);
})(VAS, jQuery);
