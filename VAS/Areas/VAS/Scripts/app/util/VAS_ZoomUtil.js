/**
 * Shared record-zoom helper for VAS widgets, panels and forms.
 *
 * VAS.ZoomUtil.zoomToRecord(PrimaryColumnName, Record_ID, AD_Window_ID, NewWindowName, OldWindowName)
 *   Opens the standard window positioned on one record.
 *     - AD_Window_ID > 0   -> zoom straight away, no server round-trip.
 *     - AD_Window_ID <= 0  -> resolve the id from the window names first
 *                             (VAS/VAS_ZoomWindow/GetWindowId -> PoReceiptTabPanelModel.GetWindowId:
 *                              new name -> old name -> VAS_ZoomScreenConfig), then zoom.
 *     - Record_ID 0        -> the window is opened without positioning on a record
 *                             (what a "new record" quick action needs).
 *     - no id and no names -> nothing happens; the caller's link simply does not navigate.
 *   Returns a promise resolved with the AD_Window_ID actually used (0 when no zoom
 *   happened), so a caller can keep the id and skip the lookup next time.
 *
 * VAS.ZoomUtil.getWindowId(NewWindowName, OldWindowName)
 *   The resolution step on its own - for callers that want the id up-front, e.g. to
 *   decide whether a document number renders as a link or as plain text.
 *
 * Resolved ids are cached per page load (and in-flight requests are shared), so
 * repeated clicks cost one request per window name pair.
 *
 * Labels / Message Keys - none (no user-facing text).
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.ZoomUtil = VAS.ZoomUtil || {};

    /* "NewWindowName|OldWindowName" -> promise of AD_Window_ID. */
    var windowIdCache = {};

    function toId(value) {
        var id = Number(value);
        return (!id || isNaN(id) || id <= 0) ? 0 : id;
    }

    function toName(value) {
        return value == null ? '' : String(value).trim();
    }

    /* Opens the window on the single record. Record_ID 0 opens the window without
       positioning on anything (a "new record" style action). Returns the id used,
       or 0 when the framework pieces or the arguments are not usable - zoom is
       best-effort. */
    function startWindow(primaryColumnName, recordId, windowId) {
        if (windowId <= 0 || recordId < 0 || !primaryColumnName) {
            return 0;
        }

        if (!window.VIS || !VIS.viewManager || typeof VIS.viewManager.startWindow !== 'function') {
            return 0;
        }

        if (recordId > 0 && typeof VIS.Query !== 'function') {
            return 0;
        }

        try {
            var zoomQuery = null;
            if (recordId > 0) {
                zoomQuery = new VIS.Query();
                zoomQuery.addRestriction(primaryColumnName, VIS.Query.prototype.EQUAL, recordId);
                zoomQuery.setRecordCount(1);
            }
            VIS.viewManager.startWindow(windowId, zoomQuery);
            return windowId;
        }
        catch (e) {
            return 0;
        }
    }

    VAS.ZoomUtil.getWindowId = function (NewWindowName, OldWindowName) {
        var newName = toName(NewWindowName);
        var oldName = toName(OldWindowName);

        if (!newName && !oldName) {
            return $.Deferred().resolve(0).promise();
        }

        var key = newName + '|' + oldName;

        if (windowIdCache[key]) {
            return windowIdCache[key];
        }

        var deferred = $.Deferred();
        windowIdCache[key] = deferred.promise();

        $.ajax({
            url: VIS.Application.contextUrl + 'VAS/VAS_ZoomWindow/GetWindowId',
            type: 'GET',
            dataType: 'json',
            cache: false,

            data: {
                newWindowName: newName,
                oldWindowName: oldName
            },

            success: function (response) {
                var data = response;

                if (typeof response === 'string') {
                    try {
                        data = JSON.parse(response);
                    }
                    catch (e) {
                        data = null;
                    }
                }

                var windowId = toId(data && data.windowId);

                /* A miss is not cached - the window may be seeded later in the session. */
                if (windowId <= 0) {
                    delete windowIdCache[key];
                }

                deferred.resolve(windowId);
            },

            error: function () {
                delete windowIdCache[key];
                deferred.resolve(0);
            }
        });

        return windowIdCache[key];
    };

    VAS.ZoomUtil.zoomToRecord = function (PrimaryColumnName, Record_ID, AD_Window_ID, NewWindowName, OldWindowName) {
        var primaryColumnName = toName(PrimaryColumnName);
        var recordId = toId(Record_ID);
        var windowId = toId(AD_Window_ID);

        if (!primaryColumnName || recordId < 0) {
            return $.Deferred().resolve(0).promise();
        }

        if (windowId > 0) {
            return $.Deferred().resolve(startWindow(primaryColumnName, recordId, windowId)).promise();
        }

        return VAS.ZoomUtil.getWindowId(NewWindowName, OldWindowName)
            .then(function (resolvedWindowId) {
                return startWindow(primaryColumnName, recordId, toId(resolvedWindowId));
            });
    };

})(VAS, jQuery);
