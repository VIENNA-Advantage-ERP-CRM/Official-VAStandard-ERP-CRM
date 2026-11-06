/**
 * VAS_307_OpenRequisitionsKPIWidget - 2x1 KPI on the Requisition screen (and its Home copy).
 * Completed requisitions with quantity still to come: count | open amount; click = list popup,
 * list row = requisition popup.
 * Backend: VAS_307_OpenRequisitionsKPIWidget/GetData
 *
 * Labels / Message Keys
 *  VAS_307_OpenRequisitions  Open Requisitions
 *  VAS_307_Reqs              reqs
 *  VAS_307_LinesStillOpen    lines still to come
 *  VAS_307_ListSub           requisitions with quantity still to come
 */
; (function (VAS, $) {
    "use strict";
    var U = VAS.RequisitionWidgetUtil;
    VAS.VAS_307_OpenRequisitionsKPIWidget = U.buildKpi({
        url: 'VAS_307_OpenRequisitionsKPIWidget/GetData',
        title: function () { return U.lbl('VAS_307_OpenRequisitions', 'Open Requisitions'); },
        tone: '',
        unit: function (d) { return U.unit(d.count, 'VAS_307_Reqs', 'reqs'); },
        meta: function (d) {
            return U.num(d.lineCount) + ' ' + U.phrase(d.lineCount, 'VAS_307_LinesStillOpen', 'lines still to come',
                function () { return U.lbl('VAS_305_Line1', 'line') + ' · ' + U.lbl('VAS_305_StillOpen', 'Still to come'); });
        },
        listTitle: function () { return U.lbl('VAS_307_OpenRequisitions', 'Open Requisitions'); },
        listSub: function (d) {
            return U.num(d.count) + ' ' + U.phrase(d.count, 'VAS_307_ListSub', 'requisitions with quantity still to come',
                function () { return U.lbl('VAS_305_Requisition1', 'requisition') + ' · ' + U.lbl('VAS_305_StillOpen', 'Still to come'); }) +
                ' · ' + U.money(d.amount, d.currency);
        }
    });
})(VAS, jQuery);
