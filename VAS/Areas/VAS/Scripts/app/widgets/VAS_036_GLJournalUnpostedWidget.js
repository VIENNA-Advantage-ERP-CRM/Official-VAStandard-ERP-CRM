/**
 * GL Journal Unposted KPI Widget
 * Purpose - Display unposted GL journals and journal details.
 *
 * -- Labels / Message Keys --------------------------------------------
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Unposted                             | VAS_036_GLJUnposted
 *  2  | Drafts waiting                       | VAS_036_DraftsWaiting
 *  3  | Unposted Journals                    | VAS_036_UnpostedJournals
 *  4  | Journals waiting to be posted        | VAS_036_UnpostedSub
 *  5  | Export                               | VAS_Export
 *  6  | Close                                | VAS_Close
 *  7  | Approve                              | VAS_036_Approve
 *  8  | Post Journal                         | VAS_036_PostJournal
 *  9  | Error Loading Data                   | VIS_Error
 * 10  | No Data                              | VIS_NoData
 * 11  | No Journal Lines                     | VAS_036_NoJournalLines
 * ---------------------------------------------------------------------
 */


; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    function lbl(key, fallback) {
        var text = VIS.Msg.getMsg(key);

        return (
            text &&
            text.charAt(0) !== "["
        )
            ? text
            : fallback;
    }

    function esc(value) {
        return String(
            value == null
                ? ""
                : value
        )
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    function normalizeResponse(result) {
        var data = result;

        if (
            data &&
            data.d !== undefined
        ) {
            data = data.d;
        }

        for (
            var index = 0;
            index < 3;
            index++
        ) {
            if (typeof data !== "string") {
                break;
            }

            try {
                data = JSON.parse(data);
            }
            catch (error) {
                return null;
            }
        }

        return data;
    }

    function formatAmount(
        amount,
        precision
    ) {
        var standardPrecision = 2;

        try {
            standardPrecision =
                Number(
                    VIS.Env
                        .getCtx()
                        .getStdPrecision()
                );
        }
        catch (error) {
            standardPrecision = 2;
        }

        var resolvedPrecision =
            Number(precision);

        if (
            isNaN(resolvedPrecision) ||
            resolvedPrecision < 0
        ) {
            resolvedPrecision =
                standardPrecision;
        }

        var numericAmount =
            Number(amount || 0);

        if (isNaN(numericAmount)) {
            numericAmount = 0;
        }

        return numericAmount.toLocaleString(
            window.navigator.language,
            {
                minimumFractionDigits:
                    resolvedPrecision,

                maximumFractionDigits:
                    resolvedPrecision
            }
        );
    }

    function isPosted(value) {
        var text =
            String(
                value == null
                    ? ""
                    : value
            ).toUpperCase();

        return (
            value === true ||
            value === 1 ||
            text === "Y" ||
            text === "TRUE" ||
            text === "1"
        );
    }

    var PILL_CLASS = {
        "DR": "VAS-glju-pill-draft",
        "CO": "VAS-glju-pill-posted",
        "CL": "VAS-glju-pill-posted",
        "IP": "VAS-glju-pill-submit",
        "AP": "VAS-glju-pill-posted",
        "NA": "VAS-glju-pill-pending",
        "VO": "VAS-glju-pill-returned",
        "RE": "VAS-glju-pill-returned"
    };

    VAS.VAS_036_GLJournalUnpostedWidget =
        function () {
            this.frame = null;
            this.windowNo = 0;
            this.AD_UserHomeWidgetID = 0;

            var $self = this;

            var $root =
                $('<div class="VAS-glju-root">');

            var $kpiValue = null;
            var $whyText = null;

            var $dialog = null;
            var $dialogBody = null;
            var $dialogFooterText = null;
            var $dialogBusy = null;

            var $detailDialog = null;
            var $detailBody = null;
            var $detailBusy = null;

            var $approveButton = null;
            var $postButton = null;

            var dialogLoaded = false;
            var detailLoaded = false;

            var selectedJournalId = 0;
            var selectedJournalStatus = "";
            var selectedJournalPosted = false;

            var actionInProgress = false;
            var refreshTimer = null;

            var countRequest = null;
            var listRequest = null;
            var detailRequest = null;
            var actionRequest = null;

            var isDisposed = false;

            var baseUrl =
                VIS.Application.contextUrl;

            this.Initalize = function () {
                createWidget();
                createBusyIndicator();

                showBusy(true);
                loadData();

                refreshTimer =
                    window.setInterval(
                        function () {
                            if (
                                !isDisposed &&
                                !actionInProgress
                            ) {
                                refreshData();
                            }
                        },
                        1000 * 60 * 5
                    );
            };

            function docIconSvg() {
                return (
                    '<svg viewBox="0 0 24 24" ' +
                    'fill="none" ' +
                    'stroke="currentColor" ' +
                    'stroke-width="2" ' +
                    'stroke-linecap="round" ' +
                    'stroke-linejoin="round">' +

                    '<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"></path>' +
                    '<polyline points="14 2 14 8 20 8"></polyline>' +
                    '<path d="M16 13H8M16 17H8"></path>' +

                    "</svg>"
                );
            }

            function createBusyIndicator() {
                var id =
                    $self.AD_UserHomeWidgetID;

                var $busy =
                    $(
                        '<div id="VAS-glju-busy-' +
                        id +
                        '" class="vis-busyindicatorouterwrap">' +

                        '<div class="vis-busyindicatorinnerwrap">' +
                        '<i class="vis_widgetloader"></i>' +
                        "</div>" +

                        "</div>"
                    );

                $root.append($busy);
            }

            function showBusy(show) {
                $root.find(
                    "#VAS-glju-busy-" +
                    $self.AD_UserHomeWidgetID
                ).toggle(show);
            }

            function showDialogBusy(show) {
                if (
                    $dialogBusy &&
                    $dialogBusy[0]
                ) {
                    $dialogBusy[0]
                        .style.visibility =
                        show
                            ? "visible"
                            : "hidden";
                }
            }

            function showDetailBusy(show) {
                if (
                    $detailBusy &&
                    $detailBusy[0]
                ) {
                    $detailBusy[0]
                        .style.visibility =
                        show
                            ? "visible"
                            : "hidden";
                }
            }

            function createWidget() {
                var id =
                    $self.AD_UserHomeWidgetID;

                var icon =
                    docIconSvg();

                var html =
                    '<div class="kpi kpi-amber" ' +
                    'role="button" tabindex="0">' +

                    '<div class="w-head">' +

                    '<div class="w-icon">' +
                    icon +
                    "</div>" +

                    '<div class="w-title">' +
                    esc(
                        lbl(
                            "VAS_036_GLJUnposted",
                            "Unposted"
                        )
                    ) +
                    "</div>" +

                    '<span class="VAS-glju-zoom">' +
                    '<svg viewBox="0 0 24 24" fill="none" ' +
                    'stroke="currentColor" stroke-width="2.6">' +
                    '<path d="M9 18l6-6-6-6"></path>' +
                    "</svg>" +
                    "</span>" +

                    "</div>" +

                    '<div class="kpi-value warning" ' +
                    'id="VAS-glju-val-' +
                    id +
                    '">&mdash;</div>' +

                    '<div class="kpi-why">' +
                    '<span class="kpi-why-text" ' +
                    'id="VAS-glju-why-' +
                    id +
                    '">' +

                    esc(
                        lbl(
                            "VAS_036_DraftsWaiting",
                            "Drafts waiting to be approved + posted."
                        )
                    ) +

                    "</span>" +
                    "</div>" +

                    "</div>";

                $root.append(html);

                $kpiValue =
                    $root.find(
                        "#VAS-glju-val-" +
                        id
                    );

                $whyText =
                    $root.find(
                        "#VAS-glju-why-" +
                        id
                    );

                $root.find(".kpi")
                    .on(
                        "click",
                        openDialog
                    )
                    .on(
                        "keydown",
                        function (event) {
                            if (
                                event.key === "Enter" ||
                                event.key === " "
                            ) {
                                event.preventDefault();
                                openDialog();
                            }
                        }
                    );

                createDialog(icon);
                createDetailDialog(icon);
            }

            function createDialog(icon) {
                var id =
                    $self.AD_UserHomeWidgetID;

                $dialog =
                    $(
                        '<div class="VAS-glju-dialog" ' +
                        'id="VAS-glju-dialog-' +
                        id +
                        '" style="display:none" ' +
                        'role="dialog" aria-modal="true">' +

                        '<div class="VAS-glju-dialog-scrim"></div>' +

                        '<div class="VAS-glju-dialog-card">' +

                        '<div class="VAS-glju-dialog-head">' +

                        '<div class="VAS-glju-dialog-icon">' +
                        icon +
                        "</div>" +

                        '<div class="VAS-glju-dialog-title-wrap">' +

                        '<div class="VAS-glju-dialog-title">' +
                        esc(
                            lbl(
                                "VAS_036_UnpostedJournals",
                                "Unposted Journals"
                            )
                        ) +
                        "</div>" +

                        '<div class="VAS-glju-dialog-sub">' +
                        esc(
                            lbl(
                                "VAS_036_UnpostedSub",
                                "Drafts, submitted and pending approval - not yet posted to GL"
                            )
                        ) +
                        "</div>" +

                        "</div>" +

                        '<button type="button" ' +
                        'class="VAS-glju-dialog-close">' +
                        `<svg viewBox="0 0 24 24"" fill="none"" stroke="currentColor"" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>` +
                        "</button>" +

                        "</div>" +

                        '<div class="VAS-glju-dialog-body">' +

                        '<div class="VAS-glju-dialog-busy">' +
                        '<div class="vis-busyindicatorinnerwrap">' +
                        '<i class="vis_widgetloader"></i>' +
                        "</div>" +
                        "</div>" +

                        '<div class="VAS-glju-table-wrap" ' +
                        'id="VAS-glju-dialog-body-' +
                        id +
                        '"></div>' +

                        "</div>" +

                        '<div class="VAS-glju-dialog-footer">' +

                        '<span class="VAS-glju-dialog-total" ' +
                        'id="VAS-glju-dialog-total-' +
                        id +
                        '"></span>' +

                        '<div class="VAS-glju-dialog-actions">' +

                        '<button type="button" ' +
                        'class="VAS-glju-export">' +
                        esc(
                            lbl(
                                "VAS_Export",
                                "Export"
                            )
                        ) +
                        "</button>" +

                        '<button type="button" ' +
                        'class="VAS-glju-close-primary">' +
                        esc(
                            lbl(
                                "VAS_Close",
                                "Close"
                            )
                        ) +
                        "</button>" +

                        "</div>" +
                        "</div>" +
                        "</div>" +
                        "</div>"
                    );

                $dialogBody =
                    $dialog.find(
                        "#VAS-glju-dialog-body-" +
                        id
                    );

                $dialogFooterText =
                    $dialog.find(
                        "#VAS-glju-dialog-total-" +
                        id
                    );

                $dialogBusy =
                    $dialog.find(
                        ".VAS-glju-dialog-busy"
                    );

                showDialogBusy(false);

                $dialog.find(
                    ".VAS-glju-dialog-close, " +
                    ".VAS-glju-close-primary, " +
                    ".VAS-glju-dialog-scrim"
                ).on(
                    "click",
                    closeDialog
                );

                $dialog.find(
                    ".VAS-glju-export"
                ).on(
                    "click",
                    exportDialogRows
                );

                /*
                 * Delegated click:
                 * remains active after table HTML is rebuilt.
                 */
                $dialogBody
                    .off(
                        "click.VAS036Detail",
                        ".VAS-glju-entry-row"
                    )
                    .on(
                        "click.VAS036Detail",
                        ".VAS-glju-entry-row",
                        function (event) {
                            event.preventDefault();
                            event.stopPropagation();

                            var journalId =
                                parseInt(
                                    $(this).attr(
                                        "data-journal-id"
                                    ),
                                    10
                                );

                            if (
                                isNaN(journalId) ||
                                journalId <= 0
                            ) {
                                return;
                            }

                            openDetailDialog(
                                journalId
                            );
                        }
                    );

                $(document).on(
                    "keydown.VAS-glju-" +
                    id,
                    function (event) {
                        if (
                            event.key !== "Escape" ||
                            actionInProgress
                        ) {
                            return;
                        }

                        if (
                            $detailDialog &&
                            $detailDialog.is(":visible")
                        ) {
                            closeDetailDialog();
                        }
                        else if (
                            $dialog &&
                            $dialog.is(":visible")
                        ) {
                            closeDialog();
                        }
                    }
                );

                $("body").append($dialog);
            }

            function createDetailDialog(icon) {
                var id =
                    $self.AD_UserHomeWidgetID;

                $detailDialog =
                    $(
                        '<div class="VAS-glju-dialog ' +
                        'VAS-glju-detail-dialog" ' +
                        'id="VAS-glju-detail-dialog-' +
                        id +
                        '" style="display:none;z-index:1000002" ' +
                        'role="dialog" aria-modal="true">' +

                        '<div class="VAS-glju-dialog-scrim"></div>' +

                        '<div class="VAS-glju-dialog-card ' +
                        'VAS-glju-detail-card">' +

                        '<div class="VAS-glju-dialog-head">' +

                        '<div class="VAS-glju-dialog-icon">' +
                        icon +
                        "</div>" +

                        '<div class="VAS-glju-dialog-title-wrap">' +

                        '<div class="VAS-glju-dialog-title" ' +
                        'id="VAS-glju-detail-title-' +
                        id +
                        '">&mdash;</div>' +

                        '<div class="VAS-glju-dialog-sub" ' +
                        'id="VAS-glju-detail-sub-' +
                        id +
                        '">&mdash;</div>' +

                        "</div>" +

                        '<button type="button" ' +
                        'class="VAS-glju-dialog-close">' +
                        `<svg viewBox="0 0 24 24"" fill="none"" stroke="currentColor"" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>` +

                        "</button>" +

                        "</div>" +

                        '<div class="VAS-glju-dialog-body ' +
                        'VAS-glju-detail-body">' +

                        '<div class="VAS-glju-dialog-busy">' +
                        '<div class="vis-busyindicatorinnerwrap">' +
                        '<i class="vis_widgetloader"></i>' +
                        "</div>" +
                        "</div>" +

                        '<div class="VAS-glju-detail-content" ' +
                        'id="VAS-glju-detail-body-' +
                        id +
                        '"></div>' +

                        "</div>" +

                        '<div class="VAS-glju-dialog-footer">' +

                        '<span class="VAS-glju-detail-footer-spacer"></span>' +

                        '<div class="VAS-glju-dialog-actions">' +

                        '<button type="button" ' +
                        'class="VAS-glju-export ' +
                        'VAS-glju-action-approve">' +
                        esc(
                            lbl(
                                "VAS_036_Approve",
                                "Approve"
                            )
                        ) +
                        "</button>" +

                        '<button type="button" ' +
                        'class="VAS-glju-close-primary ' +
                        'VAS-glju-action-post">' +
                        esc(
                            lbl(
                                "VAS_036_PostJournal",
                                "Post journal"
                            )
                        ) +
                        "</button>" +

                        '<button type="button" ' +
                        'class="VAS-glju-close-primary ' +
                        'VAS-glju-detail-close">' +
                        esc(
                            lbl(
                                "VAS_Close",
                                "Close"
                            )
                        ) +
                        "</button>" +

                        "</div>" +
                        "</div>" +
                        "</div>" +
                        "</div>"
                    );

                $detailBody =
                    $detailDialog.find(
                        "#VAS-glju-detail-body-" +
                        id
                    );

                $detailBusy =
                    $detailDialog.find(
                        ".VAS-glju-dialog-busy"
                    );

                $approveButton =
                    $detailDialog.find(
                        ".VAS-glju-action-approve"
                    );

                $postButton =
                    $detailDialog.find(
                        ".VAS-glju-action-post"
                    );

                showDetailBusy(false);

                $approveButton.on(
                    "click",
                    function () {
                        executeJournalAction(
                            "ApproveJournal",
                            "approve"
                        );
                    }
                );

                $postButton.on(
                    "click",
                    function () {
                        executeJournalAction(
                            "PostJournal",
                            "post"
                        );
                    }
                );

                $detailDialog.find(
                    ".VAS-glju-dialog-close, " +
                    ".VAS-glju-detail-close, " +
                    ".VAS-glju-dialog-scrim"
                ).on(
                    "click",
                    closeDetailDialog
                );

                $("body").append($detailDialog);

                updateActionButtons();
            }

            function loadData() {
                if (isDisposed) {
                    return;
                }

                if (
                    countRequest &&
                    countRequest.readyState !== 4
                ) {
                    countRequest.abort();
                }

                showBusy(true);

                countRequest = $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_041_GLJournalEntriesWidget/GetUnpostedCount",

                    type: "GET",
                    dataType: "json",
                    cache: false,

                    success: function (result) {
                        var data =
                            normalizeResponse(result);

                        if (
                            data &&
                            !data.error &&
                            data.success !== false
                        ) {
                            $kpiValue.text(
                                Number(
                                    data.UnpostedCount || 0
                                )
                            );

                            return;
                        }

                        $kpiValue.html("&mdash;");

                        $whyText.text(
                            data &&
                                (
                                    data.errorText ||
                                    data.error
                                )
                                ? (
                                    data.errorText ||
                                    data.error
                                )
                                : lbl(
                                    "VIS_Error",
                                    "Error loading data."
                                )
                        );
                    },

                    error: function (
                        xhr,
                        textStatus
                    ) {
                        if (textStatus === "abort") {
                            return;
                        }

                        $kpiValue.html("&mdash;");

                        $whyText.text(
                            getAjaxErrorMessage(xhr)
                        );
                    },

                    complete: function () {
                        countRequest = null;

                        if (!isDisposed) {
                            showBusy(false);
                        }
                    }
                });
            }

            function openDialog() {
                if (
                    !$dialog ||
                    isDisposed
                ) {
                    return;
                }

                $dialog.show();

                $("body").addClass(
                    "VAS-glju-body-lock"
                );

                if (!dialogLoaded) {
                    loadDialogRows();
                }
            }

            function closeDialog() {
                if (actionInProgress) {
                    return;
                }

                closeDetailDialog();

                if ($dialog) {
                    $dialog.hide();
                }

                $("body").removeClass(
                    "VAS-glju-body-lock"
                );
            }

            function openDetailDialog(
                journalId
            ) {
                journalId =
                    parseInt(
                        journalId,
                        10
                    );

                if (
                    !$detailDialog ||
                    !$detailDialog.length ||
                    isNaN(journalId) ||
                    journalId <= 0
                ) {
                    return;
                }

                selectedJournalId =
                    journalId;

                selectedJournalStatus = "";
                selectedJournalPosted = false;

                detailLoaded = false;

                $detailBody.empty();

                updateActionButtons();

                $detailDialog
                    .css(
                        "z-index",
                        "1000002"
                    )
                    .show();

                loadJournalDetail(
                    journalId
                );
            }

            function closeDetailDialog() {
                if (
                    !$detailDialog ||
                    actionInProgress
                ) {
                    return;
                }

                if (
                    detailRequest &&
                    detailRequest.readyState !== 4
                ) {
                    detailRequest.abort();
                }

                selectedJournalId = 0;
                selectedJournalStatus = "";
                selectedJournalPosted = false;

                detailLoaded = false;

                updateActionButtons();

                $detailDialog.hide();
            }

            function loadDialogRows() {
                if (isDisposed) {
                    return;
                }

                if (
                    listRequest &&
                    listRequest.readyState !== 4
                ) {
                    listRequest.abort();
                }

                showDialogBusy(true);

                listRequest = $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_041_GLJournalEntriesWidget/GetUnpostedEntries",

                    type: "GET",
                    dataType: "json",
                    cache: false,

                    success: function (result) {
                        var data =
                            normalizeResponse(result);

                        if (
                            data &&
                            !data.error &&
                            data.success !== false
                        ) {
                            renderDialog(data);
                            dialogLoaded = true;
                        }
                        else {
                            renderDialogError(
                                data &&
                                (
                                    data.errorText ||
                                    data.error
                                )
                            );
                        }
                    },

                    error: function (
                        xhr,
                        textStatus
                    ) {
                        if (textStatus === "abort") {
                            return;
                        }

                        renderDialogError(
                            getAjaxErrorMessage(xhr)
                        );
                    },

                    complete: function () {
                        listRequest = null;

                        if (!isDisposed) {
                            showDialogBusy(false);
                        }
                    }
                });
            }

            function renderDialog(data) {
                var rows =
                    Array.isArray(data.Entries)
                        ? data.Entries
                        : [];

                var symbol =
                    data.CurSymbol ||
                    data.ISOCode ||
                    "";

                var precision =
                    Number(data.StdPrecision);

                if (!rows.length) {
                    $dialogBody.html(
                        '<div class="VAS-glju-dialog-empty">' +
                        esc(
                            lbl(
                                "VIS_NoData",
                                "No data available."
                            )
                        ) +
                        "</div>"
                    );

                    $dialogFooterText.text("");
                    return;
                }

                var html =
                    '<table class="VAS-glju-dialog-table">' +
                    "<thead><tr>" +

                    "<th>Journal No.</th>" +
                    "<th>Date</th>" +
                    "<th>Description</th>" +
                    "<th>Status</th>" +
                    "<th>Total Debit</th>" +
                    "<th>Total Credit</th>" +

                    "</tr></thead><tbody>";

                for (
                    var index = 0;
                    index < rows.length;
                    index++
                ) {
                    var row = rows[index];

                    var status =
                        row.StatusName ||
                        row.DocStatus ||
                        "";

                    var pillClass =
                        PILL_CLASS[row.DocStatus] ||
                        "VAS-glju-pill-draft";

                    html +=
                        '<tr class="VAS-glju-entry-row" ' +
                        'role="button" tabindex="0" ' +
                        'data-journal-id="' +
                        esc(row.GL_Journal_ID) +
                        '">' +

                        '<td class="VAS-glju-doc">' +
                        esc(row.DocumentNo) +
                        "</td>" +

                        '<td class="VAS-glju-date">' +
                        esc(row.DateAcct) +
                        "</td>" +

                        '<td class="VAS-glju-desc">' +
                        esc(row.Description) +
                        "</td>" +

                        "<td>" +
                        '<span class="VAS-glju-pill ' +
                        pillClass +
                        '">' +
                        "<span></span>" +
                        esc(status) +
                        "</span>" +
                        "</td>" +

                        '<td class="VAS-glju-amt">' +
                        esc(
                            symbol +
                            formatAmount(
                                row.TotalDebit,
                                precision
                            )
                        ) +
                        "</td>" +

                        '<td class="VAS-glju-amt">' +
                        esc(
                            symbol +
                            formatAmount(
                                row.TotalCredit,
                                precision
                            )
                        ) +
                        "</td>" +

                        "</tr>";
                }

                html +=
                    "</tbody></table>";

                $dialogBody.html(html);

                $dialogFooterText.text(
                    rows.length +
                    " journals � total " +
                    symbol +
                    formatAmount(
                        data.TotalDebit,
                        precision
                    )
                );
            }

            function loadJournalDetail(
                journalId
            ) {
                if (
                    detailRequest &&
                    detailRequest.readyState !== 4
                ) {
                    detailRequest.abort();
                }

                detailLoaded = false;

                showDetailBusy(true);
                updateActionButtons();

                detailRequest = $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_041_GLJournalEntriesWidget/GetJournalEntryDetail",

                    type: "GET",
                    dataType: "json",
                    cache: false,

                    data: {
                        journalId:
                            journalId
                    },

                    success: function (result) {
                        var data =
                            normalizeResponse(result);

                        if (
                            data &&
                            !data.error &&
                            data.success !== false
                        ) {
                            renderJournalDetail(data);
                        }
                        else {
                            renderDetailError(
                                data &&
                                (
                                    data.errorText ||
                                    data.error
                                )
                            );
                        }
                    },

                    error: function (
                        xhr,
                        textStatus
                    ) {
                        if (textStatus === "abort") {
                            return;
                        }

                        renderDetailError(
                            getAjaxErrorMessage(xhr)
                        );
                    },

                    complete: function () {
                        detailRequest = null;

                        if (!isDisposed) {
                            showDetailBusy(false);
                        }
                    }
                });
            }

            function renderJournalDetail(data) {
                var journal =
                    data.Journal || {};

                var lines =
                    Array.isArray(data.Lines)
                        ? data.Lines
                        : [];

                var symbol =
                    data.CurSymbol ||
                    data.ISOCode ||
                    "";

                var precision =
                    Number(data.StdPrecision);

                selectedJournalId =
                    parseInt(
                        journal.GL_Journal_ID ||
                        selectedJournalId,
                        10
                    );

                selectedJournalStatus =
                    String(
                        journal.DocStatus || ""
                    ).toUpperCase();

                selectedJournalPosted =
                    isPosted(journal.Posted);

                var statusText =
                    journal.StatusName ||
                    journal.DocStatus ||
                    "";

                var pillClass =
                    PILL_CLASS[
                    selectedJournalStatus
                    ] ||
                    "VAS-glju-pill-draft";

                var totalDebit =
                    symbol +
                    formatAmount(
                        journal.TotalDebit,
                        precision
                    );

                var totalCredit =
                    symbol +
                    formatAmount(
                        journal.TotalCredit,
                        precision
                    );

                var id =
                    $self.AD_UserHomeWidgetID;

                $detailDialog.find(
                    "#VAS-glju-detail-title-" +
                    id
                ).text(
                    (
                        journal.DocumentNo ||
                        ""
                    ) +
                    " � " +
                    (
                        journal.Description ||
                        ""
                    )
                );

                $detailDialog.find(
                    "#VAS-glju-detail-sub-" +
                    id
                ).text(
                    statusText +
                    " � " +
                    (
                        journal.DateAcct ||
                        ""
                    )
                );

                var html =
                    '<div class="VAS-glju-detail-summary">' +

                    "<div><span>Journal No.</span><strong>" +
                    esc(journal.DocumentNo) +
                    "</strong></div>" +

                    "<div><span>Date</span><strong>" +
                    esc(journal.DateAcct) +
                    "</strong></div>" +

                    "<div><span>Status</span><strong>" +
                    '<span class="VAS-glju-pill ' +
                    pillClass +
                    '"><span></span>' +
                    esc(statusText) +
                    "</span></strong></div>" +

                    "<div><span>Accounting Book</span><strong>" +
                    esc(
                        journal.AccountingBook ||
                        "Primary"
                    ) +
                    "</strong></div>" +

                    "<div><span>Total Debit</span><strong>" +
                    esc(totalDebit) +
                    "</strong></div>" +

                    "<div><span>Total Credit</span><strong>" +
                    esc(totalCredit) +
                    "</strong></div>" +

                    '<div class="VAS-glju-detail-description">' +
                    "<span>Description</span><strong>" +
                    esc(journal.Description) +
                    "</strong></div>" +

                    "</div>" +

                    '<div class="VAS-glju-detail-section-title">' +
                    "Journal Lines" +
                    "</div>" +

                    '<div class="VAS-glju-detail-lines-wrap">' +
                    '<table class="VAS-glju-detail-lines">' +

                    "<thead><tr>" +
                    "<th>Account</th>" +
                    "<th>Debit</th>" +
                    "<th>Credit</th>" +
                    "<th>Cost Center</th>" +
                    "<th>Business Partner</th>" +
                    "<th>Product</th>" +
                    "<th>Project</th>" +
                    "</tr></thead><tbody>";

                if (!lines.length) {
                    html +=
                        '<tr><td colspan="7">' +
                        esc(
                            lbl(
                                "VAS_036_NoJournalLines",
                                "No journal lines."
                            )
                        ) +
                        "</td></tr>";
                }
                else {
                    for (
                        var index = 0;
                        index < lines.length;
                        index++
                    ) {
                        var line = lines[index];

                        var account =
                            line.AccountCode &&
                                line.AccountName
                                ? (
                                    line.AccountCode +
                                    " � " +
                                    line.AccountName
                                )
                                : (
                                    line.AccountCode ||
                                    line.AccountName ||
                                    "-"
                                );

                        html +=
                            "<tr>" +

                            "<td>" +
                            esc(account) +
                            "</td>" +

                            '<td class="VAS-glju-amt">' +
                            esc(
                                Number(
                                    line.Debit || 0
                                ) > 0
                                    ? (
                                        symbol +
                                        formatAmount(
                                            line.Debit,
                                            precision
                                        )
                                    )
                                    : "-"
                            ) +
                            "</td>" +

                            '<td class="VAS-glju-amt">' +
                            esc(
                                Number(
                                    line.Credit || 0
                                ) > 0
                                    ? (
                                        symbol +
                                        formatAmount(
                                            line.Credit,
                                            precision
                                        )
                                    )
                                    : "-"
                            ) +
                            "</td>" +

                            "<td>" +
                            esc(
                                line.CostCenter ||
                                "-"
                            ) +
                            "</td>" +

                            "<td>" +
                            esc(
                                line.BPartner ||
                                "-"
                            ) +
                            "</td>" +

                            "<td>" +
                            esc(
                                line.Product ||
                                "-"
                            ) +
                            "</td>" +

                            "<td>" +
                            esc(
                                line.Project ||
                                "-"
                            ) +
                            "</td>" +

                            "</tr>";
                    }
                }

                html +=
                    "</tbody>" +

                    "<tfoot><tr>" +

                    "<td>Total</td>" +

                    '<td class="VAS-glju-amt">' +
                    esc(totalDebit) +
                    "</td>" +

                    '<td class="VAS-glju-amt">' +
                    esc(totalCredit) +
                    "</td>" +

                    '<td colspan="4"></td>' +

                    "</tr></tfoot>" +

                    "</table>" +
                    "</div>" +

                    '<div class="VAS-glju-created-strip">' +

                    '<span class="VAS-glju-avatar">' +
                    esc(
                        initials(
                            journal.CreatedByName
                        )
                    ) +
                    "</span>" +

                    "<div>" +
                    "<span>Created By</span>" +
                    "<strong>" +
                    esc(
                        journal.CreatedByName ||
                        "-"
                    ) +
                    "</strong>" +

                    (
                        journal.CreatedDate
                            ? (
                                " � drafted " +
                                esc(
                                    journal.CreatedDate
                                )
                            )
                            : ""
                    ) +

                    "</div>" +
                    "</div>";

                $detailBody.html(html);

                detailLoaded = true;

                updateActionButtons();
            }

            function updateActionButtons() {
                var status =
                    String(
                        selectedJournalStatus || ""
                    ).toUpperCase();

                var canApprove =
                    detailLoaded &&
                    !selectedJournalPosted &&
                    (
                        status === "DR" ||
                        status === "IP" ||
                        status === "NA"
                    );

                var canPost =
                    detailLoaded &&
                    !selectedJournalPosted &&
                    (
                        status === "AP" ||
                        status === "CO" ||
                        status === "CL"
                    );

                if ($approveButton) {
                    $approveButton
                        .toggle(canApprove)
                        .prop(
                            "disabled",
                            actionInProgress ||
                            !canApprove
                        );
                }

                if ($postButton) {
                    $postButton
                        .toggle(canPost)
                        .prop(
                            "disabled",
                            actionInProgress ||
                            !canPost
                        );
                }
            }

            function executeJournalAction(
                actionName,
                actionType
            ) {
                if (
                    actionInProgress ||
                    selectedJournalId <= 0
                ) {
                    return;
                }

                actionInProgress = true;

                showDetailBusy(true);
                updateActionButtons();

                actionRequest = $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_041_GLJournalEntriesWidget/" +
                        actionName,

                    type: "POST",
                    dataType: "json",
                    cache: false,

                    data: {
                        journalId:
                            selectedJournalId
                    },

                    success: function (result) {
                        var data =
                            normalizeResponse(result);

                        if (
                            !data ||
                            data.success === false ||
                            data.error
                        ) {
                            window.alert(
                                (
                                    data &&
                                    (
                                        data.errorText ||
                                        data.error ||
                                        data.message
                                    )
                                ) ||
                                "Journal process failed."
                            );

                            return;
                        }

                        dialogLoaded = false;

                        loadData();
                        loadDialogRows();

                        loadJournalDetail(
                            selectedJournalId
                        );
                    },

                    error: function (xhr) {
                        window.alert(
                            getAjaxErrorMessage(xhr)
                        );
                    },

                    complete: function () {
                        actionRequest = null;
                        actionInProgress = false;

                        showDetailBusy(false);
                        updateActionButtons();
                    }
                });
            }

            function getAjaxErrorMessage(xhr) {
                var response =
                    normalizeResponse(
                        xhr &&
                        xhr.responseText
                    );

                if (response) {
                    return (
                        response.errorText ||
                        response.error ||
                        response.message ||
                        "Error loading data."
                    );
                }

                return xhr && xhr.status
                    ? (
                        "Request failed. HTTP " +
                        xhr.status
                    )
                    : "Error loading data.";
            }

            function renderDialogError(message) {
                dialogLoaded = false;

                $dialogBody.html(
                    '<div class="VAS-glju-dialog-empty">' +
                    esc(
                        message ||
                        "Error loading data."
                    ) +
                    "</div>"
                );

                $dialogFooterText.text("");
            }

            function renderDetailError(message) {
                detailLoaded = false;

                selectedJournalStatus = "";
                selectedJournalPosted = false;

                $detailBody.html(
                    '<div class="VAS-glju-dialog-empty">' +
                    esc(
                        message ||
                        "Error loading journal details."
                    ) +
                    "</div>"
                );

                updateActionButtons();
            }

            function initials(name) {
                var parts =
                    String(name || "")
                        .trim()
                        .split(/\s+/);

                if (
                    !parts.length ||
                    !parts[0]
                ) {
                    return "--";
                }

                if (parts.length === 1) {
                    return parts[0]
                        .charAt(0)
                        .toUpperCase();
                }

                return (
                    parts[0].charAt(0) +
                    parts[1].charAt(0)
                ).toUpperCase();
            }

            function exportDialogRows() {
                var $table =
                    $dialogBody.find(
                        ".VAS-glju-dialog-table"
                    );

                if (!$table.length) {
                    return;
                }

                var html =
                    '<html xmlns:o="urn:schemas-microsoft-com:office:office" ' +
                    'xmlns:x="urn:schemas-microsoft-com:office:excel" ' +
                    'xmlns="http://www.w3.org/TR/REC-html40">' +
                    '<head><meta charset="utf-8"></head>' +
                    "<body>" +
                    $table[0].outerHTML +
                    "</body></html>";

                var blob =
                    new Blob(
                        [html],
                        {
                            type:
                                "application/vnd.ms-excel;charset=utf-8;"
                        }
                    );

                var url =
                    URL.createObjectURL(blob);

                var link =
                    document.createElement("a");

                link.href = url;
                link.download =
                    "unposted-journals.xls";

                document.body.appendChild(link);
                link.click();
                document.body.removeChild(link);

                URL.revokeObjectURL(url);
            }

            function refreshData() {
                dialogLoaded = false;

                $kpiValue.html("&mdash;");

                loadData();

                if (
                    $dialog &&
                    $dialog.is(":visible")
                ) {
                    loadDialogRows();
                }
            }

            this.refreshData =
                refreshData;

            this.getRoot =
                function () {
                    return $root;
                };

            this.disposeComponent =
                function () {
                    isDisposed = true;

                    if (refreshTimer) {
                        window.clearInterval(
                            refreshTimer
                        );
                    }

                    if (
                        countRequest &&
                        countRequest.readyState !== 4
                    ) {
                        countRequest.abort();
                    }

                    if (
                        listRequest &&
                        listRequest.readyState !== 4
                    ) {
                        listRequest.abort();
                    }

                    if (
                        detailRequest &&
                        detailRequest.readyState !== 4
                    ) {
                        detailRequest.abort();
                    }

                    if (
                        actionRequest &&
                        actionRequest.readyState !== 4
                    ) {
                        actionRequest.abort();
                    }

                    $(document).off(
                        "keydown.VAS-glju-" +
                        $self.AD_UserHomeWidgetID
                    );

                    $("body").removeClass(
                        "VAS-glju-body-lock"
                    );

                    if ($detailDialog) {
                        $detailDialog.remove();
                    }

                    if ($dialog) {
                        $dialog.remove();
                    }

                    $root.remove();
                };
        };

    VAS.VAS_036_GLJournalUnpostedWidget
        .prototype.refreshWidget =
        function () {
            if (
                typeof this.refreshData ===
                "function"
            ) {
                this.refreshData();
            }
        };

    VAS.VAS_036_GLJournalUnpostedWidget
        .prototype.init =
        function (
            windowNo,
            frame
        ) {
            this.frame = frame;

            this.AD_UserHomeWidgetID =
                frame
                    .widgetInfo
                    .AD_UserHomeWidgetID;

            this.windowNo =
                windowNo;

            this.Initalize();

            this.frame
                .getContentGrid()
                .append(
                    this.getRoot()
                );
        };

    VAS.VAS_036_GLJournalUnpostedWidget
        .prototype.widgetSizeChange =
        function () {
        };

    VAS.VAS_036_GLJournalUnpostedWidget
        .prototype.dispose =
        function () {
            this.disposeComponent();

            if (this.frame) {
                this.frame.dispose();
            }

            this.frame = null;
        };

})(VAS, jQuery);