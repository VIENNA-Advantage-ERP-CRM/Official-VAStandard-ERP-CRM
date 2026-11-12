/**
 * VAS_309_ReqNotYetOrderedWidget - 2x1 KPI on the Requisition screen (and its Home copy).
 * Completed-requisition lines with quantity on no PO and no Material Transfer:
 * count of lines | amount; click = list popup.
 * Backend: VAS_309_ReqNotYetOrderedWidget/GetData
 *
 * Labels / Message Keys
 *  VAS_309_NotYetOrdered  Not Yet Ordered
 *  VAS_309_Lines          lines
 *  VAS_309_NoPOOrTransfer No PO or transfer yet
 *  VAS_309_OnReqs         requisitions
 */
; (function (VAS, $) {
    "use strict";
    var U = VAS.RequisitionWidgetUtil;
    VAS.VAS_309_ReqNotYetOrderedWidget = U.buildKpi({
        url: 'VAS_309_ReqNotYetOrderedWidget/GetData',
        title: function () { return U.lbl('VAS_309_NotYetOrdered', 'Not Yet Ordered'); },
        tone: '',
        unit: function (d) { return U.unit(d.count, 'VAS_309_Lines', 'lines'); },
        meta: function (d) { return U.lbl('VAS_309_NoPOOrTransfer', 'No PO or transfer yet') + ' · ' + U.num(d.requisitions) + ' ' + U.unit(d.requisitions, 'VAS_309_OnReqs', 'requisitions'); },
        listTitle: function () { return U.lbl('VAS_309_NotYetOrdered', 'Not Yet Ordered'); },
        listSub: function (d) {
            return U.num(d.count) + ' ' + U.unit(d.count, 'VAS_309_Lines', 'lines') + ' · ' +
                U.num(d.requisitions) + ' ' + U.unit(d.requisitions, 'VAS_309_OnReqs', 'requisitions') + ' · ' + U.money(d.amount, d.currency);
        }
    });
})(VAS, jQuery);
