/**
 * VAS_308_ReqOrderedNotReceivedWidget - 2x1 KPI on the Requisition screen (and its Home copy).
 * Requisition lines on a PO without full GRN, or on a Material Transfer not yet completed:
 * count of lines | amount still to receive; click = list popup.
 * Backend: VAS_308_ReqOrderedNotReceivedWidget/GetData
 *
 * Labels / Message Keys
 *  VAS_308_OrderedNotReceived  Ordered, Not Received
 *  VAS_308_Lines               lines
 *  VAS_308_OnPOOrTransfer      On PO or transfer
 *  VAS_308_PastNeedBy          past need-by
 *  VAS_308_ListSub             lines on PO / transfer not received
 */
; (function (VAS, $) {
    "use strict";
    var U = VAS.RequisitionWidgetUtil;
    VAS.VAS_308_ReqOrderedNotReceivedWidget = U.buildKpi({
        url: 'VAS_308_ReqOrderedNotReceivedWidget/GetData',
        title: function () { return U.lbl('VAS_308_OrderedNotReceived', 'Ordered, Not Received'); },
        tone: 'amb',
        unit: function (d) { return U.unit(d.count, 'VAS_308_Lines', 'lines'); },
        meta: function (d) {
            return U.lbl('VAS_308_OnPOOrTransfer', 'On PO or transfer') + ' · ' + U.num(d.pastNeedBy) + ' ' + U.lbl('VAS_308_PastNeedBy', 'past need-by');
        },
        listTitle: function () { return U.lbl('VAS_308_OrderedNotReceived', 'Ordered, Not Received'); },
        listSub: function (d) {
            var reqs = (d.records || []).length;
            return U.num(d.count) + ' ' + U.phrase(d.count, 'VAS_308_ListSub', 'lines on PO / transfer not received',
                function () { return U.lbl('VAS_305_Line1', 'line') + ' · ' + U.lbl('VAS_308_OnPOOrTransfer', 'On PO or transfer'); }) +
                ' · ' + U.num(reqs) + ' ' + U.unit(reqs, 'VAS_314_Requisitions', 'requisitions') + ' · ' + U.money(d.amount, d.currency);
        }
    });
})(VAS, jQuery);
