/**
 * VAS_315_CategoryWiseRequisitionWidget - 3x2 grid on the Requisition screen (and its Home copy).
 * Requested value of completed / closed requisitions of the last 12 months by product category,
 * largest first, with a share bar + percentage and the number of requisitions. Rows that do not fit
 * are paged; a row opens the category's lines (12 per page, VAS163 2026-11-20), a line opens the
 * requisition popup. Popup keys reused: VAS_305_Attribute, VAS_305_UOM, VAS_305_Showing, VAS_305_Of.
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

        /** VAS163 2026-11-20: Sr 74 - share in the browser culture's percent format. */
        function pctText(p) {
            try { return (p / 100).toLocaleString(window.navigator.language || 'en-US', { style: 'percent', maximumFractionDigits: 0 }); }
            catch (e) { return p + '%'; }
        }

        this.Initalize = function () {
            // VAS163 2026-11-20: Sr 65 - no top-category badge (pill) in the header any more
            $card = $('<div class="vas-rqw-card">' +
                U.header(U.icon.grid, U.lbl('VAS_315_CategoryWise', 'Category wise Requisition'), U.lbl('VAS_315_Subtitle', 'Product categories · last 12 months by value'), null) +
                // VAS163 2026-11-20: Sr 74 - share column holds the bar and the percentage
                '<div class="vas-rqw-tb" style="--vas-rqw-cols:minmax(0,1.5fr) minmax(0,1.2fr) 6.6em 2.8em"></div>' +
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
            var cats = data.categories || [], cur = data.currency;
            var max = cats.length ? cats[0].amount : 0;
            // VAS163 2026-11-20: Sr 67 / Sr 63 - the Value heading carries no currency sign (the amounts show it);
            // Sr 74 - percentage next to the share bar; Sr 72 / Sr 75 - hover title on every cell
            U.renderRows({
                $tb: $tb, $help: $help, $pager: $pager, state: state, rows: cats,
                emptyText: U.lbl('VAS_315_Empty', 'No requisitions in the last 12 months.'),
                head: U.gridHead([U.lbl('VAS_315_Category', 'Category'), U.lbl('VAS_315_Share', 'Share'),
                    U.lbl('VAS_315_Value', 'Value'), U.lbl('VAS_315_Reqs', 'Reqs')], [2, 3]),
                cell: function (c) {
                    var pct = pctText(data.total > 0 ? Math.round(c.amount / data.total * 100) : 0);
                    var w = max > 0 ? (c.amount / max * 100) : 0;
                    var reqs = U.num(c.requisitions);
                    return '<span class="b" title="' + U.esc(c.name) + '">' + U.esc(c.name) + '</span>' +
                        '<span class="vas-rqw-shr" title="' + U.esc(pct) + '"><span class="vas-rqw-bar"><i style="width:' + w.toFixed(1) + '%"></i></span><em>' + U.esc(pct) + '</em></span>' +
                        '<span class="vas-rqw-r" title="' + U.esc(U.docMoney(c.amount, cur, false)) + '">' + U.esc(U.money(c.amount, cur)) + '</span>' +
                        '<span class="vas-rqw-r" title="' + U.esc(reqs) + '">' + U.esc(reqs) + '</span>';
                },
                helpText: function (a, b, n) {
                    return U.lbl('VAS_305_Showing', 'Showing') + ' ' + a + '–' + b + ' ' + U.lbl('VAS_305_Of', 'of') + ' ' + n + ' ' + U.unit(n, 'VAS_315_Categories', 'categories');
                },
                onClick: function (c) { openCategory(c); }
            });
        };

        /** VAS163 2026-11-20: Sr 71 - rows per page of the category lines popup. */
        var LINES_PAGE = 12;

        /**
         * Lines of one category in a popup.
         * VAS163 2026-11-20: Sr 73 - Attribute column right after Product, only when a line has an attribute set instance;
         * Sr 70 - UOM column right after Qty; Sr 66 - date with the year; Sr 67 - Amount heading without a currency sign;
         * Sr 68 - line amount in the requisition's price-list currency with its sign; Sr 71 - 12 rows per page;
         * Sr 72 / Sr 75 - hover title on every cell.
         */
        function openCategory(c) {
            var cur = data.currency, lines = c.lines || [], page = 0;
            var pages = Math.max(1, Math.ceil(lines.length / LINES_PAGE));
            var showAttr = lines.some(function (l) { return !!l.attribute; });
            var td = function (cls, text) {
                return '<span' + (cls ? ' class="' + cls + '"' : '') + ' title="' + U.esc(text) + '">' + U.esc(text) + '</span>';
            };
            var rowHtml = function (l) {
                var chip = U.stateChip(l.state, l.needBy);
                return '<div class="vas-rqw-gr" data-r="' + l.requisitionId + '">' + td('b', l.documentNo) + td('', l.product) +
                    (showAttr ? td('m', l.attribute || '') : '') +
                    td('vas-rqw-r', U.num(l.qty)) + td('m', l.uom || '') +
                    td('vas-rqw-r', U.date(l.needBy)) +
                    td('vas-rqw-r vas-rqw-wrap', U.rowMoney(l, cur)) +
                    '<span title="' + U.esc($('<div>').html(chip).text()) + '">' + chip + '</span></div>';
            };
            var heads = [U.lbl('VAS_305_Requisition', 'Requisition'), U.lbl('VAS_305_Product', 'Product')];
            var cols = 'minmax(0,1fr) minmax(0,1.8fr)';
            if (showAttr) { heads.push(U.lbl('VAS_305_Attribute', 'Attribute')); cols += ' minmax(0,1.1fr)'; }
            heads.push(U.lbl('VAS_305_Qty', 'Qty'), U.lbl('VAS_305_UOM', 'UOM'), U.lbl('VAS_305_NeedBy', 'Need by'), U.lbl('VAS_305_Amount', 'Amount'), U.lbl('VAS_305_Status', 'Status'));
            cols += ' 4.4em minmax(3.5em,0.6fr) 6.6em minmax(7.5em,1.1fr) 7.6em';
            var right = showAttr ? [3, 5, 6] : [2, 4, 5];
            var listHtml = function () {
                var start = page * LINES_PAGE;
                var slice = lines.slice(start, start + LINES_PAGE);
                var html = U.table(cols, right, heads, slice.map(rowHtml));
                if (pages > 1) {
                    html += '<div class="vas-rqw-lpg"><span class="vas-rqw-hp">' + U.esc(U.lbl('VAS_305_Showing', 'Showing') + ' ' + (start + 1) + '–' + (start + slice.length) + ' ' +
                        U.lbl('VAS_305_Of', 'of') + ' ' + lines.length) + '</span><span class="vas-rqw-pg">' +
                        '<button type="button" data-lp="-1" aria-label="' + U.esc(U.lbl('VAS_305_PrevPage', 'Previous page')) + '"' + (page ? '' : ' disabled') + '>' + U.icon.prev + '</button>' +
                        (page + 1) + ' ' + U.esc(U.lbl('VAS_305_Of', 'of')) + ' ' + pages +
                        '<button type="button" data-lp="1" aria-label="' + U.esc(U.lbl('VAS_305_NextPage', 'Next page')) + '"' + (page < pages - 1 ? '' : ' disabled') + '>' + U.icon.next + '</button></span></div>';
                }
                return html;
            };
            var bodyHtml = function () {
                return '<div class="vas-rqw-sh"><b>' + U.esc(c.name) + '</b><span>' + U.esc(U.num(c.lineCount) + ' ' + U.phrase(c.lineCount, 'VAS_315_LinesOn', 'lines on', function () { return U.lbl('VAS_305_Line1', 'line') + ' ·'; }) + ' ' +
                    U.num(c.requisitions) + ' ' + U.unit(c.requisitions, 'VAS_314_Requisitions', 'requisitions') + ' · ' + U.money(c.amount, cur)) + '</span></div>' +
                    '<div class="vas-rqw-lst">' + listHtml() + '</div>';
            };
            var view = {
                title: c.name,
                body: bodyHtml(),
                after: function ($m) {
                    var $pb = $m.find('.vas-rqw-pb');
                    $pb.off('click.rqwc').on('click.rqwc', '.vas-rqw-gr[data-r]', function () {
                        U.showRequisition(self, Number($(this).attr('data-r')));
                    });
                    $pb.off('click.rqwcp').on('click.rqwcp', '[data-lp]', function () {
                        page = Math.max(0, Math.min(pages - 1, page + Number($(this).attr('data-lp'))));
                        $pb.find('.vas-rqw-lst').html(listHtml());
                        view.body = bodyHtml(); // Back from a requisition returns to this page
                        $pb.scrollTop(0);
                    });
                }
            };
            U.openModal(view);
        }

        this.getRoot = function () { return $root; };
        this.disposeComponent = function () { if (stopResize) { stopResize(); } $root.remove(); };
    };

    U.makeWidget(VAS.VAS_315_CategoryWiseRequisitionWidget);
})(VAS, jQuery);
