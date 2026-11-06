/**
 * VAS_310_LateRequisitionsWidget - 2x1 KPI on the Requisition screen (and its Home copy).
 * Completed requisitions past their need-by date with quantity still to come:
 * count | amount still to come on the late lines; click = list popup.
 * Backend: VAS_310_LateRequisitionsWidget/GetData
 *
 * Labels / Message Keys
 *  VAS_310_LateRequisitions  Late Requisitions
 *  VAS_310_Reqs              reqs
 *  VAS_310_PastNeedBy        Past need-by, not received
 *  VAS_310_Oldest            oldest
 *  VAS_310_Days              days
 */
; (function (VAS, $) {
    "use strict";
    var U = VAS.RequisitionWidgetUtil;
    VAS.VAS_310_LateRequisitionsWidget = U.buildKpi({
        url: 'VAS_310_LateRequisitionsWidget/GetData',
        title: function () { return U.lbl('VAS_310_LateRequisitions', 'Late Requisitions'); },
        tone: 'red',
        unit: function (d) { return U.unit(d.count, 'VAS_310_Reqs', 'reqs'); },
        meta: function (d) {
            return U.lbl('VAS_310_PastNeedBy', 'Past need-by, not received') +
                (d.oldestDays > 0 ? ' · ' + U.lbl('VAS_310_Oldest', 'oldest') + ' ' + d.oldestDays + ' ' + U.unit(d.oldestDays, 'VAS_310_Days', 'days') : '');
        },
        listTitle: function () { return U.lbl('VAS_310_LateRequisitions', 'Late Requisitions'); },
        listSub: function (d) {
            return U.num(d.count) + ' ' + U.unit(d.count, 'VAS_314_Requisitions', 'requisitions') + ' · ' +
                U.num(d.lineCount) + ' ' + U.unit(d.lineCount, 'VAS_309_Lines', 'lines') + ' · ' + U.money(d.amount, d.currency);
        }
    });
})(VAS, jQuery);
