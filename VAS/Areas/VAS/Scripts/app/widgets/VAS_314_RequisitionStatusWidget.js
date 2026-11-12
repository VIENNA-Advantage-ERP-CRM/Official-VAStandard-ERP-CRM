/**
 * VAS_314_RequisitionStatusWidget - 3x2 donut on the Requisition screen (and its Home copy).
 * Requisitions of the last 12 months by the system requisition status (Open, Partially Ordered,
 * Ordered, Partially Delivered, Delivered - evaluated live) plus Draft and Closed / Voided.
 * Pastel palette (checked for colour-blind separation; legend carries the labels and values).
 * Hover = tooltip, slice / legend click = list popup, list row = requisition popup.
 * Backend: VAS_314_RequisitionStatusWidget/GetStatusSummary
 *
 * Labels / Message Keys
 *  VAS_314_RequisitionsByStatus  Requisitions by Status
 *  VAS_314_Subtitle              Last 12 months · count and value
 *  VAS_314_Total                 total
 *  VAS_314_Requisitions          requisitions
 *  VAS_314_Draft                 Draft             (server)
 *  VAS_314_ClosedVoided          Closed / Voided   (server)
 *  VAS_314_Empty                 No requisitions in the last 12 months.
 */
; (function (VAS, $) {
    "use strict";
    var U = VAS.RequisitionWidgetUtil;
    // pastel, one hue family per status; order = slice order (validated: CVD dE 11.9, normal dE 15.1)
    var COLORS = { OP: '#7AB6EE', PO: '#F2BC62', OR: '#5FB8C9', PD: '#C9D86F', DE: '#5CC2A0', DR: '#A797EE', CL: '#F6A08E' };

    VAS.VAS_314_RequisitionStatusWidget = function () {
        var self = this;
        var $root = $('<div class="vas-rqw-root">');
        var $card, $pie, $tip = null, data = null;
        this.frame = null;
        this.windowNo = -1;

        this.Initalize = function () {
            $card = $('<div class="vas-rqw-card">' +
                U.header(U.icon.pie, U.lbl('VAS_314_RequisitionsByStatus', 'Requisitions by Status'), U.lbl('VAS_314_Subtitle', 'Last 12 months · count and value'), 'tot') +
                '<div class="vas-rqw-pie"><div class="vas-rqw-donut"></div><div class="vas-rqw-pl"></div></div>' +
                '<span class="vas-rqw-busy"><i></i></span></div>');
            $root.empty().append($card);
            $pie = $card.find('.vas-rqw-pie');
            self._stopWatch = U.whenRemoved($root[0], function () { self.disposeComponent(); });
            self.reload();
        };

        this.reload = function () {
            if (!$card) { return; }
            $card.find('.vas-rqw-busy').addClass('on');
            U.get('VAS_314_RequisitionStatusWidget/GetStatusSummary').done(function (d) {
                data = d;
                $card.find('[data-pill="tot"]').text(U.money(d.amount, d.currency) + ' ' + U.lbl('VAS_314_Total', 'total'));
                render();
            }).fail(function (err) {
                data = null;
                $pie.find('.vas-rqw-donut').empty();
                $pie.find('.vas-rqw-pl').html('<div class="vas-rqw-msg err">' + U.esc(err) + '</div>');
            }).always(function () { $card.find('.vas-rqw-busy').removeClass('on'); });
        };

        function render() {
            var slices = data.slices || [], total = data.total || 0, cur = data.currency;
            var C = 2 * Math.PI * 60, gap = total > 1 ? 2.2 : 0, off = 0;
            var arcs = '';
            if (total > 0) {
                slices.forEach(function (s) {
                    if (!s.count) { return; }
                    var len = s.count / total * C, dash = Math.max(len - gap, 0.5);
                    arcs += '<circle class="sl" data-s="' + s.code + '" cx="80" cy="80" r="60" fill="none" stroke="' + COLORS[s.code] + '" stroke-width="24"' +
                        ' stroke-dasharray="' + dash.toFixed(2) + ' ' + (C - dash).toFixed(2) + '" stroke-dashoffset="' + (-off).toFixed(2) + '" transform="rotate(-90 80 80)"></circle>';
                    off += len;
                });
            } else {
                arcs = '<circle cx="80" cy="80" r="60" fill="none" stroke="#EDF2F7" stroke-width="24"></circle>';
            }
            $pie.find('.vas-rqw-donut').html('<svg viewBox="0 0 160 160" role="img" aria-label="' + U.esc(U.lbl('VAS_314_RequisitionsByStatus', 'Requisitions by Status')) + '">' + arcs +
                '<text x="80" y="80" text-anchor="middle" font-size="28" font-weight="600" fill="#102C3F" font-family="Roboto, sans-serif">' + U.esc(U.num(total)) + '</text>' +
                '<text x="80" y="98" text-anchor="middle" font-size="10.5" fill="#5F7283" font-family="Roboto, sans-serif">' + U.esc(U.unit(total, 'VAS_314_Requisitions', 'requisitions')) + '</text></svg>');
            $pie.find('.vas-rqw-pl').html(total ? slices.map(function (s) {
                return '<button type="button" class="vas-rqw-plr" data-s="' + s.code + '" title="' + U.esc(s.name) + '"><i style="background:' + COLORS[s.code] + '"></i>' +
                    '<span>' + U.esc(s.name) + '</span><b>' + U.num(s.count) + '</b><em>' + U.esc(U.money(s.amount, cur)) + '</em></button>';
            }).join('') : '<div class="vas-rqw-msg">' + U.esc(U.lbl('VAS_314_Empty', 'No requisitions in the last 12 months.')) + '</div>');

            var by = {};
            slices.forEach(function (s) { by[s.code] = s; });
            var hi = function (code) {
                $pie.toggleClass('hov', !!code);
                $pie.find('[data-s]').each(function () { $(this).toggleClass('on', $(this).attr('data-s') === code); });
            };
            if (!$tip) { $tip = $('<div class="vas-rqw-tip"></div>').appendTo('body'); }
            $pie.off('.rqw')
                .on('mouseover.rqw', '[data-s]', function () { hi($(this).attr('data-s')); })
                .on('mouseleave.rqw', function () { hi(null); $tip.hide(); })
                .on('mousemove.rqw', function (e) {
                    var $t = $(e.target).closest('circle.sl');
                    if (!$t.length) { $tip.hide(); return; }
                    var s = by[$t.attr('data-s')];
                    $tip.html('<b>' + U.esc(s.name) + '</b> · ' + U.num(s.count) + ' · ' + U.esc(U.money(s.amount, cur)) + ' · ' + Math.round(s.count / total * 100) + '%')
                        .css({ left: e.clientX + 14, top: e.clientY + 12 }).show();
                })
                .on('click.rqw', '[data-s]', function () {
                    $tip.hide();
                    var s = by[$(this).attr('data-s')];
                    if (s) { U.showList(self, s.name, U.num(s.count) + ' ' + U.unit(s.count, 'VAS_314_Requisitions', 'requisitions') + ' · ' + U.money(s.amount, cur), s.records, cur); }
                });
        }

        this.relayout = function () { };
        this.getRoot = function () { return $root; };
        this.disposeComponent = function () {
            if (self._stopWatch) { self._stopWatch(); self._stopWatch = null; }
            if ($tip) { $tip.remove(); $tip = null; }
            $root.remove();
        };
    };

    U.makeWidget(VAS.VAS_314_RequisitionStatusWidget);
})(VAS, jQuery);
