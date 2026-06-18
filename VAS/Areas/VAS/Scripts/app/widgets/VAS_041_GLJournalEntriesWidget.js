
/**
 * GL Journal Entries KPI Widget
 * Displays the monthly GL Journal count.
 * Supports:
 * 1. Monthly drill-down
 * 2. Journal details
 * 3. Approve Journal
 * 4. Post Journal
 * 5. Export monthly rows
 * 6. Print / Save detail popup as PDF
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    function lbl(key, fallback) {
        var text =
            VIS.Msg.getMsg(key);

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
            index < 2;
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
            typeof precision === "number" &&
            precision >= 0
                ? precision
                : standardPrecision;

        if (
            isNaN(resolvedPrecision) ||
            resolvedPrecision < 0
        ) {
            resolvedPrecision = 2;
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
        if (
            value === true ||
            value === 1
        ) {
            return true;
        }

        var text =
            String(
                value == null
                    ? ""
                    : value
            ).toUpperCase();

        return (
            text === "Y" ||
            text === "TRUE" ||
            text === "1"
        );
    }

    var PILL_CLASS = {
        "DR": "VAS-glje-pill-draft",
        "CO": "VAS-glje-pill-posted",
        "CL": "VAS-glje-pill-posted",
        "IP": "VAS-glje-pill-submit",
        "AP": "VAS-glje-pill-posted",
        "NA": "VAS-glje-pill-pending",
        "VO": "VAS-glje-pill-voided",
        "RE": "VAS-glje-pill-returned"
    };

    VAS.VAS_041_GLJournalEntriesWidget =
        function () {
            this.frame = null;
            this.windowNo = 0;
            this.AD_UserHomeWidgetID = 0;

            var $self = this;

            var $root =
                $(
                    '<div class="VAS-glje-root">'
                );

            var $kpiValue = null;
            var $whyText = null;
            var $titleElement = null;

            var $dialog = null;
            var $dialogBody = null;
            var $dialogFooterText = null;
            var $dialogBusy = null;

            var $detailDialog = null;
            var $detailBody = null;
            var $detailBusy = null;

            var $approveButton = null;
            var $postButton = null;
            var $downloadButton = null;
            var $detailCloseButton = null;

            var dialogLoaded = false;
            var detailLoaded = false;

            var selectedJournalId = 0;
            var selectedJournalStatus = "";
            var selectedJournalPosted = false;

            var journalActionInProgress =
                false;

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
                                !journalActionInProgress
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
                var $busy =
                    $(
                        '<div id="VAS-glje-busy-' +
                        $self.AD_UserHomeWidgetID +
                        '" class="vis-busyindicatorouterwrap">' +

                        '<div class="vis-busyindicatorinnerwrap">' +
                        '<i class="vis_widgetloader"></i>' +
                        "</div>" +

                        "</div>"
                    );

                $root.append($busy);
            }

            function showBusy(show) {
                var $busy =
                    $root.find(
                        "#VAS-glje-busy-" +
                        $self.AD_UserHomeWidgetID
                    );

                if (show) {
                    $busy.show();
                }
                else {
                    $busy.hide();
                }
            }

            function showDialogBusy(show) {
                if (
                    !$dialogBusy ||
                    !$dialogBusy[0]
                ) {
                    return;
                }

                $dialogBusy[0]
                    .style.visibility =
                    show
                        ? "visible"
                        : "hidden";
            }

            function showDetailBusy(show) {
                if (
                    !$detailBusy ||
                    !$detailBusy[0]
                ) {
                    return;
                }

                $detailBusy[0]
                    .style.visibility =
                    show
                        ? "visible"
                        : "hidden";
            }

            function createWidget() {
                var svgIcon =
                    docIconSvg();

                var id =
                    $self.AD_UserHomeWidgetID;

                var html =
                    '<div class="kpi kpi-blue" ' +
                    'role="button" tabindex="0">' +

                    '<div class="w-head">' +

                    '<div class="w-icon">' +
                    svgIcon +
                    "</div>" +

                    '<div class="w-title" ' +
                    'id="VAS-glje-title-' +
                    id +
                    '">' +

                    esc(
                        lbl(
                            "VAS_041_GLJEntries",
                            "Entries"
                        )
                    ) +

                    " &middot; &mdash;" +

                    "</div>" +

                    '<span class="VAS-glje-zoom" ' +
                    'aria-hidden="true">' +

                    '<svg viewBox="0 0 24 24" ' +
                    'fill="none" ' +
                    'stroke="currentColor" ' +
                    'stroke-width="2.6" ' +
                    'stroke-linecap="round" ' +
                    'stroke-linejoin="round">' +

                    '<path d="M9 18l6-6-6-6"></path>' +

                    "</svg>" +

                    "</span>" +

                    "</div>" +

                    '<div class="kpi-value" ' +
                    'id="VAS-glje-val-' +
                    id +
                    '">&mdash;</div>' +

                    '<div class="kpi-why">' +

                    '<span class="kpi-why-text" ' +
                    'id="VAS-glje-why-' +
                    id +
                    '">&mdash;</span>' +

                    "</div>" +
                    "</div>";

                $root.append(html);

                $kpiValue =
                    $root.find(
                        "#VAS-glje-val-" +
                        id
                    );

                $whyText =
                    $root.find(
                        "#VAS-glje-why-" +
                        id
                    );

                $titleElement =
                    $root.find(
                        "#VAS-glje-title-" +
                        id
                    );

                $root.find(
                    ".kpi"
                ).on(
                    "click",
                    openDialog
                );

                $root.find(
                    ".kpi"
                ).on(
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

                createDialog(
                    svgIcon
                );

                createDetailDialog(
                    svgIcon
                );
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
                        "VAS/VAS_041_GLJournalEntriesWidget/GetMonthlyEntryCount",

                    type: "GET",
                    dataType: "json",
                    cache: false,

                    success: function (result) {
                        if (isDisposed) {
                            return;
                        }

                        var data =
                            normalizeResponse(
                                result
                            );

                        if (
                            !data ||
                            data.error ||
                            data.success === false
                        ) {
                            $kpiValue.html(
                                "&mdash;"
                            );

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

                            return;
                        }

                        $titleElement.html(
                            esc(
                                lbl(
                                    "VAS_041_GLJEntries",
                                    "Entries"
                                )
                            ) +
                            " &middot; " +
                            esc(
                                data.MonthAbbr ||
                                ""
                            )
                        );

                        $kpiValue.text(
                            typeof data.EntryCount ===
                            "number"
                                ? data.EntryCount
                                : 0
                        );

                        $whyText.text(
                            lbl(
                                "VAS_041_AllJournalEntries",
                                "All journal entries posted in"
                            ) +
                            " " +
                            (
                                data.MonthName ||
                                ""
                            ) +
                            "."
                        );
                    },

                    error: function (
                        xhr,
                        textStatus
                    ) {
                        if (
                            isDisposed ||
                            textStatus === "abort"
                        ) {
                            return;
                        }

                        $kpiValue.html(
                            "&mdash;"
                        );

                        $whyText.text(
                            lbl(
                                "VIS_Error",
                                "Error loading data."
                            )
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

            function createDialog(
                svgIcon
            ) {
                var id =
                    $self.AD_UserHomeWidgetID;

                var title =
                    lbl(
                        "VAS_041_GLJEntries",
                        "Entries"
                    ) +
                    " — " +
                    lbl(
                        "VAS_041_ThisMonth",
                        "This Month"
                    );

                $dialog =
                    $(
                        '<div class="VAS-glje-dialog" ' +
                        'id="VAS-glje-dialog-' +
                        id +
                        '" style="display:none" ' +
                        'role="dialog" ' +
                        'aria-modal="true">' +

                        '<div class="VAS-glje-dialog-scrim"></div>' +

                        '<div class="VAS-glje-dialog-card">' +

                        '<div class="VAS-glje-dialog-head">' +

                        '<div class="VAS-glje-dialog-icon">' +
                        svgIcon +
                        "</div>" +

                        '<div class="VAS-glje-dialog-title-wrap">' +

                        '<div class="VAS-glje-dialog-title">' +
                        esc(title) +
                        "</div>" +

                        '<div class="VAS-glje-dialog-sub" ' +
                        'id="VAS-glje-dialog-sub-' +
                        id +
                        '">&mdash;</div>' +

                        "</div>" +

                        '<button type="button" ' +
                        'class="VAS-glje-dialog-close" ' +
                        'aria-label="' +
                        esc(
                            lbl(
                                "VAS_Close",
                                "Close"
                            )
                        ) +
                        '">' +

                        '<svg viewBox="0 0 24 24" ' +
                        'fill="none" ' +
                        'stroke="currentColor" ' +
                        'stroke-width="2" ' +
                        'stroke-linecap="round" ' +
                        'stroke-linejoin="round">' +

                        '<line x1="18" y1="6" x2="6" y2="18"></line>' +
                        '<line x1="6" y1="6" x2="18" y2="18"></line>' +

                        "</svg>" +
                        "</button>" +

                        "</div>" +

                        '<div class="VAS-glje-dialog-body">' +

                        '<div class="VAS-glje-dialog-busy">' +

                        '<div class="vis-busyindicatorinnerwrap">' +
                        '<i class="vis_widgetloader"></i>' +
                        "</div>" +

                        "</div>" +

                        '<div class="VAS-glje-table-wrap" ' +
                        'id="VAS-glje-dialog-body-' +
                        id +
                        '"></div>' +

                        "</div>" +

                        '<div class="VAS-glje-dialog-footer">' +

                        '<span class="VAS-glje-dialog-total" ' +
                        'id="VAS-glje-dialog-total-' +
                        id +
                        '"></span>' +

                        '<div class="VAS-glje-dialog-actions">' +

                        '<button type="button" ' +
                        'class="VAS-glje-export">' +

                        esc(
                            lbl(
                                "VAS_Export",
                                "Export"
                            )
                        ) +

                        "</button>" +

                        '<button type="button" ' +
                        'class="VAS-glje-close-primary">' +

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
                        "#VAS-glje-dialog-body-" +
                        id
                    );

                $dialogFooterText =
                    $dialog.find(
                        "#VAS-glje-dialog-total-" +
                        id
                    );

                $dialogBusy =
                    $dialog.find(
                        ".VAS-glje-dialog-busy"
                    );

                showDialogBusy(false);

                $dialog.find(
                    ".VAS-glje-dialog-close, " +
                    ".VAS-glje-close-primary, " +
                    ".VAS-glje-dialog-scrim"
                ).on(
                    "click",
                    closeDialog
                );

                $dialog.find(
                    ".VAS-glje-export"
                ).on(
                    "click",
                    exportDialogRows
                );

                $(document).on(
                    "keydown.VAS-glje-" +
                    id,
                    function (event) {
                        if (
                            event.key === "Escape" &&
                            !journalActionInProgress
                        ) {
                            if (
                                $detailDialog &&
                                $detailDialog.is(
                                    ":visible"
                                )
                            ) {
                                closeDetailDialog();
                            }
                            else if (
                                $dialog &&
                                $dialog.is(
                                    ":visible"
                                )
                            ) {
                                closeDialog();
                            }
                        }
                    }
                );

                $("body").append(
                    $dialog
                );
            }

            function createDetailDialog(
                svgIcon
            ) {
                var id =
                    $self.AD_UserHomeWidgetID;

                $detailDialog =
                    $(
                        '<div class="VAS-glje-dialog ' +
                        'VAS-glje-detail-dialog" ' +
                        'id="VAS-glje-detail-dialog-' +
                        id +
                        '" style="display:none" ' +
                        'role="dialog" ' +
                        'aria-modal="true">' +

                        '<div class="VAS-glje-dialog-scrim"></div>' +

                        '<div class="VAS-glje-dialog-card ' +
                        'VAS-glje-detail-card">' +

                        '<div class="VAS-glje-dialog-head">' +

                        '<div class="VAS-glje-dialog-icon">' +
                        svgIcon +
                        "</div>" +

                        '<div class="VAS-glje-dialog-title-wrap">' +

                        '<div class="VAS-glje-dialog-title" ' +
                        'id="VAS-glje-detail-title-' +
                        id +
                        '">&mdash;</div>' +

                        '<div class="VAS-glje-dialog-sub" ' +
                        'id="VAS-glje-detail-sub-' +
                        id +
                        '">&mdash;</div>' +

                        "</div>" +

                        '<button type="button" ' +
                        'class="VAS-glje-dialog-close" ' +
                        'aria-label="' +
                        esc(
                            lbl(
                                "VAS_Close",
                                "Close"
                            )
                        ) +
                        '">' +

                        '<svg viewBox="0 0 24 24" ' +
                        'fill="none" ' +
                        'stroke="currentColor" ' +
                        'stroke-width="2" ' +
                        'stroke-linecap="round" ' +
                        'stroke-linejoin="round">' +

                        '<line x1="18" y1="6" x2="6" y2="18"></line>' +
                        '<line x1="6" y1="6" x2="18" y2="18"></line>' +

                        "</svg>" +
                        "</button>" +

                        "</div>" +

                        '<div class="VAS-glje-dialog-body ' +
                        'VAS-glje-detail-body">' +

                        '<div class="VAS-glje-dialog-busy">' +

                        '<div class="vis-busyindicatorinnerwrap">' +
                        '<i class="vis_widgetloader"></i>' +
                        "</div>" +

                        "</div>" +

                        '<div class="VAS-glje-detail-content" ' +
                        'id="VAS-glje-detail-body-' +
                        id +
                        '"></div>' +

                        "</div>" +

                        '<div class="VAS-glje-dialog-footer">' +

                        '<button type="button" ' +
                        'class="VAS-glje-export ' +
                        'VAS-glje-download">' +

                        esc(
                            lbl(
                                "VAS_DownloadPDF",
                                "Download PDF"
                            )
                        ) +

                        "</button>" +

                        '<div class="VAS-glje-dialog-actions">' +

                        '<button type="button" ' +
                        'class="VAS-glje-export ' +
                        'VAS-glje-action-approve">' +

                        esc(
                            lbl(
                                "VAS_041_Approve",
                                "Approve"
                            )
                        ) +

                        "</button>" +

                        '<button type="button" ' +
                        'class="VAS-glje-close-primary ' +
                        'VAS-glje-action-post">' +

                        esc(
                            lbl(
                                "VAS_041_PostJournal",
                                "Post journal"
                            )
                        ) +

                        "</button>" +

                        '<button type="button" ' +
                        'class="VAS-glje-close-primary ' +
                        'VAS-glje-detail-close">' +

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
                        "#VAS-glje-detail-body-" +
                        id
                    );

                $detailBusy =
                    $detailDialog.find(
                        ".VAS-glje-dialog-busy"
                    );

                $approveButton =
                    $detailDialog.find(
                        ".VAS-glje-action-approve"
                    );

                $postButton =
                    $detailDialog.find(
                        ".VAS-glje-action-post"
                    );

                $downloadButton =
                    $detailDialog.find(
                        ".VAS-glje-download"
                    );

                $detailCloseButton =
                    $detailDialog.find(
                        ".VAS-glje-detail-close"
                    );

                showDetailBusy(false);
                updateActionButtons();

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

                $downloadButton.on(
                    "click",
                    printCurrentPopup
                );

                $detailDialog.find(
                    ".VAS-glje-dialog-close, " +
                    ".VAS-glje-detail-close, " +
                    ".VAS-glje-dialog-scrim"
                ).on(
                    "click",
                    closeDetailDialog
                );

                $("body").append(
                    $detailDialog
                );
            }

            function openDialog() {
                if (!$dialog) {
                    return;
                }

                $dialog.show();

                $("body").addClass(
                    "VAS-glje-body-lock"
                );

                if (!dialogLoaded) {
                    loadDialogRows();
                }
            }

            function closeDialog() {
                if (
                    !$dialog ||
                    journalActionInProgress
                ) {
                    return;
                }

                closeDetailDialog();

                $dialog.hide();

                $("body").removeClass(
                    "VAS-glje-body-lock"
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

                updateActionButtons();

                $detailDialog.show();

                loadJournalDetail(
                    journalId
                );
            }

            function closeDetailDialog() {
                if (
                    !$detailDialog ||
                    journalActionInProgress
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
                        "VAS/VAS_041_GLJournalEntriesWidget/GetMonthlyEntries",

                    type: "GET",
                    dataType: "json",
                    cache: false,

                    success: function (result) {
                        if (isDisposed) {
                            return;
                        }

                        var data =
                            normalizeResponse(
                                result
                            );

                        if (
                            data &&
                            !data.error &&
                            data.success !== false
                        ) {
                            renderDialog(
                                data
                            );

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
                        if (
                            isDisposed ||
                            textStatus === "abort"
                        ) {
                            return;
                        }

                        renderDialogError();
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
                    Array.isArray(
                        data.Entries
                    )
                        ? data.Entries
                        : [];

                var symbol =
                    data.CurSymbol ||
                    data.ISOCode ||
                    "";

                var precision =
                    Number(
                        data.StdPrecision
                    );

                var monthName =
                    data.MonthName || "";

                var year =
                    data.Year || "";

                $dialog.find(
                    "#VAS-glje-dialog-sub-" +
                    $self.AD_UserHomeWidgetID
                ).text(
                    "All GL journal vouchers created in " +
                    monthName +
                    " " +
                    year
                );

                if (!rows.length) {
                    $dialogBody.html(
                        '<div class="VAS-glje-dialog-empty">' +
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
                    '<table class="VAS-glje-dialog-table">' +

                    "<thead>" +
                    "<tr>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_041_JournalNo",
                            "Journal No."
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Date",
                            "Date"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Description",
                            "Description"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Status",
                            "Status"
                        )
                    ) +
                    "</th>" +

                    '<th class="VAS-glje-num">' +
                    esc(
                        lbl(
                            "VAS_041_TotalDebit",
                            "Total Debit"
                        )
                    ) +
                    "</th>" +

                    '<th class="VAS-glje-num">' +
                    esc(
                        lbl(
                            "VAS_041_TotalCredit",
                            "Total Credit"
                        )
                    ) +
                    "</th>" +

                    "</tr>" +
                    "</thead>" +

                    "<tbody>";

                for (
                    var index = 0;
                    index < rows.length;
                    index++
                ) {
                    var row =
                        rows[index];

                    var statusText =
                        row.StatusName ||
                        row.DocStatus ||
                        "";

                    var pillClass =
                        PILL_CLASS[
                            row.DocStatus
                        ] ||
                        "VAS-glje-pill-draft";

                    var debit =
                        symbol +
                        formatAmount(
                            row.TotalDebit,
                            precision
                        );

                    var credit =
                        symbol +
                        formatAmount(
                            row.TotalCredit,
                            precision
                        );

                    html +=
                        '<tr class="VAS-glje-entry-row" ' +
                        'data-journal-id="' +
                        row.GL_Journal_ID +
                        '" title="' +
                        esc(
                            row.DocumentNo +
                            " - " +
                            statusText +
                            ", " +
                            debit
                        ) +
                        '">' +

                        '<td class="VAS-glje-doc">' +
                        esc(
                            row.DocumentNo
                        ) +
                        "</td>" +

                        '<td class="VAS-glje-date">' +
                        esc(
                            row.DateAcct
                        ) +
                        "</td>" +

                        '<td class="VAS-glje-desc">' +
                        esc(
                            row.Description
                        ) +
                        "</td>" +

                        "<td>" +

                        '<span class="VAS-glje-pill ' +
                        pillClass +
                        '">' +

                        "<span></span>" +

                        esc(
                            statusText
                        ) +

                        "</span>" +

                        "</td>" +

                        '<td class="VAS-glje-amt">' +
                        esc(debit) +
                        "</td>" +

                        '<td class="VAS-glje-amt">' +
                        esc(credit) +
                        "</td>" +

                        "</tr>";
                }

                html +=
                    "</tbody>" +
                    "</table>";

                $dialogBody.html(html);

                $dialogBody.find(
                    ".VAS-glje-entry-row"
                ).on(
                    "click",
                    function () {
                        openDetailDialog(
                            $(this).data(
                                "journal-id"
                            )
                        );
                    }
                );

                $dialogFooterText.text(
                    rows.length +
                    " journals · total " +
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

                updateActionButtons();
                showDetailBusy(true);
                $detailBody.empty();

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
                        if (isDisposed) {
                            return;
                        }

                        var data =
                            normalizeResponse(
                                result
                            );

                        if (
                            data &&
                            !data.error &&
                            data.success !== false
                        ) {
                            renderJournalDetail(
                                data
                            );
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
                        if (
                            isDisposed ||
                            textStatus === "abort"
                        ) {
                            return;
                        }

                        renderDetailError();
                    },

                    complete: function () {
                        detailRequest = null;

                        if (!isDisposed) {
                            showDetailBusy(false);
                        }
                    }
                });
            }

            function renderJournalDetail(
                data
            ) {
                var journal =
                    data.Journal || {};

                var lines =
                    Array.isArray(
                        data.Lines
                    )
                        ? data.Lines
                        : [];

                var symbol =
                    data.CurSymbol ||
                    data.ISOCode ||
                    "";

                var precision =
                    Number(
                        data.StdPrecision
                    );

                selectedJournalId =
                    parseInt(
                        journal.GL_Journal_ID ||
                        selectedJournalId,
                        10
                    );

                selectedJournalStatus =
                    String(
                        journal.DocStatus ||
                        ""
                    ).toUpperCase();

                selectedJournalPosted =
                    isPosted(
                        journal.Posted
                    );

                var statusText =
                    journal.StatusName ||
                    journal.DocStatus ||
                    selectedJournalStatus ||
                    "";

                var pillClass =
                    PILL_CLASS[
                        selectedJournalStatus
                    ] ||
                    "VAS-glje-pill-draft";

                var id =
                    $self.AD_UserHomeWidgetID;

                $detailDialog.find(
                    "#VAS-glje-detail-title-" +
                    id
                ).text(
                    (
                        journal.DocumentNo ||
                        ""
                    ) +
                    " · " +
                    (
                        journal.Description ||
                        ""
                    )
                );

                $detailDialog.find(
                    "#VAS-glje-detail-sub-" +
                    id
                ).text(
                    statusText +
                    " · " +
                    (
                        journal.DateAcct ||
                        ""
                    )
                );

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

                var accountingBook =
                    (
                        journal.AccountingBook ||
                        "Primary"
                    ) +
                    (
                        data.ISOCode
                            ? (
                                " · " +
                                data.ISOCode
                            )
                            : ""
                    );

                var html =
                    '<div class="VAS-glje-detail-summary">' +

                    "<div>" +
                    "<span>Journal No.</span>" +
                    "<strong>" +
                    esc(
                        journal.DocumentNo
                    ) +
                    "</strong>" +
                    "</div>" +

                    "<div>" +
                    "<span>Date</span>" +
                    "<strong>" +
                    esc(
                        journal.DateAcct
                    ) +
                    "</strong>" +
                    "</div>" +

                    "<div>" +
                    "<span>Status</span>" +
                    "<strong>" +

                    '<span class="VAS-glje-pill ' +
                    pillClass +
                    '">' +

                    "<span></span>" +

                    esc(
                        statusText
                    ) +

                    "</span>" +

                    "</strong>" +
                    "</div>" +

                    "<div>" +
                    "<span>Accounting Book</span>" +
                    "<strong>" +
                    esc(
                        accountingBook
                    ) +
                    "</strong>" +
                    "</div>" +

                    "<div>" +
                    "<span>Total Debit</span>" +
                    "<strong>" +
                    esc(
                        totalDebit
                    ) +
                    "</strong>" +
                    "</div>" +

                    "<div>" +
                    "<span>Total Credit</span>" +
                    "<strong>" +
                    esc(
                        totalCredit
                    ) +
                    "</strong>" +
                    "</div>" +

                    '<div class="VAS-glje-detail-description">' +
                    "<span>Description</span>" +
                    "<strong>" +
                    esc(
                        journal.Description
                    ) +
                    "</strong>" +
                    "</div>" +

                    "</div>" +

                    '<div class="VAS-glje-detail-section-title">' +
                    "Journal Lines" +
                    "</div>" +

                    '<div class="VAS-glje-detail-lines-wrap">' +

                    '<table class="VAS-glje-detail-lines">' +

                    "<thead>" +
                    "<tr>" +

                    "<th>Account</th>" +
                    "<th>Debit</th>" +
                    "<th>Credit</th>" +
                    "<th>Cost Center</th>" +
                    "<th>Business Partner</th>" +
                    "<th>Product</th>" +
                    "<th>Project</th>" +

                    "</tr>" +
                    "</thead>" +

                    "<tbody>";

                if (!lines.length) {
                    html +=
                        "<tr>" +
                        '<td colspan="7">' +
                        esc(
                            lbl(
                                "VAS_044_NoJournalLines",
                                "No journal lines."
                            )
                        ) +
                        "</td>" +
                        "</tr>";
                }
                else {
                    for (
                        var index = 0;
                        index < lines.length;
                        index++
                    ) {
                        var line =
                            lines[index];

                        var accountText =
                            "";

                        if (
                            line.AccountCode &&
                            line.AccountName
                        ) {
                            accountText =
                                line.AccountCode +
                                " · " +
                                line.AccountName;
                        }
                        else {
                            accountText =
                                line.AccountCode ||
                                line.AccountName ||
                                "-";
                        }

                        html +=
                            "<tr>" +

                            "<td>" +
                            esc(
                                accountText
                            ) +
                            "</td>" +

                            '<td class="VAS-glje-amt">' +
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

                            '<td class="VAS-glje-amt">' +
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

                    "<tfoot>" +
                    "<tr>" +

                    "<td>Total</td>" +

                    '<td class="VAS-glje-amt">' +
                    esc(
                        totalDebit
                    ) +
                    "</td>" +

                    '<td class="VAS-glje-amt">' +
                    esc(
                        totalCredit
                    ) +
                    "</td>" +

                    '<td colspan="4"></td>' +

                    "</tr>" +
                    "</tfoot>" +

                    "</table>" +
                    "</div>" +

                    '<div class="VAS-glje-created-strip">' +

                    '<span class="VAS-glje-avatar">' +
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
                                " · drafted " +
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
                        selectedJournalStatus ||
                        ""
                    ).toUpperCase();

                var canApprove =
                    !selectedJournalPosted &&
                    (
                        status === "DR" ||
                        status === "IP" ||
                        status === "NA"
                    );

                var canPost =
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
                            journalActionInProgress ||
                            !canApprove ||
                            selectedJournalId <= 0
                        );
                }

                if ($postButton) {
                    $postButton
                        .toggle(canPost)
                        .prop(
                            "disabled",
                            journalActionInProgress ||
                            !canPost ||
                            selectedJournalId <= 0
                        );
                }

                if ($downloadButton) {
                    $downloadButton.prop(
                        "disabled",
                        journalActionInProgress ||
                        selectedJournalId <= 0 ||
                        !detailLoaded
                    );
                }

                if ($detailCloseButton) {
                    $detailCloseButton.prop(
                        "disabled",
                        journalActionInProgress
                    );
                }
            }

            function executeJournalAction(
                actionName,
                actionType
            ) {
                if (
                    journalActionInProgress ||
                    selectedJournalId <= 0
                ) {
                    return;
                }

                var status =
                    String(
                        selectedJournalStatus ||
                        ""
                    ).toUpperCase();

                if (
                    actionType === "approve" &&
                    status !== "DR" &&
                    status !== "IP" &&
                    status !== "NA"
                ) {
                    return;
                }

                if (
                    actionType === "post" &&
                    status !== "AP" &&
                    status !== "CO" &&
                    status !== "CL"
                ) {
                    return;
                }

                journalActionInProgress = true;

                setActionBusy(
                    true,
                    actionType
                );

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
                            normalizeResponse(
                                result
                            );

                        if (
                            !data ||
                            data.success === false ||
                            data.error
                        ) {
                            showProcessError(
                                (
                                    data &&
                                    (
                                        data.errorText ||
                                        data.error ||
                                        data.message
                                    )
                                ) ||
                                lbl(
                                    "VAS_044_JournalProcessFailed",
                                    "Journal process failed."
                                )
                            );

                            return;
                        }

                        selectedJournalStatus =
                            String(
                                data.docStatus ||
                                selectedJournalStatus
                            ).toUpperCase();

                        selectedJournalPosted =
                            isPosted(
                                data.posted
                            );

                        journalActionInProgress = false;

                        setActionBusy(
                            false,
                            actionType
                        );

                        dialogLoaded = false;

                        loadData();

                        if (
                            $dialog &&
                            $dialog.is(
                                ":visible"
                            )
                        ) {
                            loadDialogRows();
                        }

                        loadJournalDetail(
                            selectedJournalId
                        );
                    },

                    error: function (
                        xhr,
                        textStatus
                    ) {
                        if (
                            textStatus === "abort"
                        ) {
                            return;
                        }

                        var response =
                            normalizeResponse(
                                xhr &&
                                xhr.responseText
                            );

                        showProcessError(
                            (
                                response &&
                                (
                                    response.errorText ||
                                    response.error ||
                                    response.message
                                )
                            ) ||
                            lbl(
                                "VAS_044_JournalProcessFailed",
                                "Journal process failed."
                            )
                        );
                    },

                    complete: function () {
                        actionRequest = null;

                        if (
                            journalActionInProgress
                        ) {
                            journalActionInProgress = false;

                            setActionBusy(
                                false,
                                actionType
                            );
                        }
                    }
                });
            }

            function setActionBusy(
                busy,
                actionType
            ) {
                showDetailBusy(busy);

                if ($approveButton) {
                    $approveButton.text(
                        busy &&
                        actionType === "approve"
                            ? lbl(
                                "VAS_044_Approving",
                                "Approving..."
                            )
                            : lbl(
                                "VAS_041_Approve",
                                "Approve"
                            )
                    );
                }

                if ($postButton) {
                    $postButton.text(
                        busy &&
                        actionType === "post"
                            ? lbl(
                                "VAS_044_Posting",
                                "Posting..."
                            )
                            : lbl(
                                "VAS_041_PostJournal",
                                "Post journal"
                            )
                    );
                }

                updateActionButtons();
            }

            function printCurrentPopup() {
                if (
                    !$detailDialog ||
                    !$detailDialog.is(
                        ":visible"
                    ) ||
                    selectedJournalId <= 0 ||
                    !detailLoaded
                ) {
                    showProcessError(
                        lbl(
                            "VAS_044_DetailsNotLoaded",
                            "Journal details are not loaded."
                        )
                    );

                    return;
                }

                var $popupCopy =
                    $detailDialog
                        .find(
                            ".VAS-glje-detail-card"
                        )
                        .first()
                        .clone();

                if (
                    !$popupCopy ||
                    !$popupCopy.length
                ) {
                    showProcessError(
                        lbl(
                            "VAS_044_DetailsNotAvailable",
                            "Journal details are not available."
                        )
                    );

                    return;
                }

                $popupCopy.find(
                    ".VAS-glje-dialog-footer"
                ).remove();

                $popupCopy.find(
                    ".VAS-glje-dialog-close"
                ).remove();

                $popupCopy.find(
                    ".VAS-glje-dialog-busy"
                ).remove();

                $popupCopy.css({
                    "position": "static",
                    "display": "block",
                    "transform": "none",
                    "width": "100%",
                    "max-width": "none",
                    "height": "auto",
                    "max-height": "none",
                    "margin": "0",
                    "overflow": "visible"
                });

                $popupCopy.find(
                    ".VAS-glje-detail-body, " +
                    ".VAS-glje-detail-content, " +
                    ".VAS-glje-detail-lines-wrap"
                ).css({
                    "display": "block",
                    "height": "auto",
                    "max-height": "none",
                    "overflow": "visible"
                });

                var printFrame =
                    document.createElement(
                        "iframe"
                    );

                printFrame.setAttribute(
                    "title",
                    "GL Journal Print"
                );

                printFrame.style.position =
                    "fixed";

                printFrame.style.left =
                    "-10000px";

                printFrame.style.top =
                    "0";

                printFrame.style.width =
                    "1px";

                printFrame.style.height =
                    "1px";

                printFrame.style.border =
                    "0";

                printFrame.style.opacity =
                    "0";

                printFrame.style.pointerEvents =
                    "none";

                document.body.appendChild(
                    printFrame
                );

                var printWindow =
                    printFrame.contentWindow;

                var printDocument =
                    printWindow.document;

                var stylesHtml = "";

                $(
                    "link[rel='stylesheet'], style"
                ).each(
                    function () {
                        stylesHtml +=
                            this.outerHTML;
                    }
                );

                var baseHref =
                    document.baseURI ||
                    window.location.href;

                var printTitle =
                    $detailDialog.find(
                        ".VAS-glje-dialog-title"
                    ).text() ||
                    "GL Journal";

                printDocument.open();

                printDocument.write(
                    "<!DOCTYPE html>" +
                    "<html>" +

                    "<head>" +

                    "<meta charset='utf-8'>" +

                    "<base href='" +
                    esc(baseHref) +
                    "'>" +

                    "<title>" +
                    esc(printTitle) +
                    "</title>" +

                    stylesHtml +

                    "<style>" +

                    "@page {" +
                    "size: A4 landscape;" +
                    "margin: 10mm;" +
                    "}" +

                    "html," +
                    "body {" +
                    "width: 100% !important;" +
                    "height: auto !important;" +
                    "margin: 0 !important;" +
                    "padding: 0 !important;" +
                    "overflow: visible !important;" +
                    "background: #ffffff !important;" +
                    "}" +

                    ".VAS-glje-print-dialog {" +
                    "position: static !important;" +
                    "display: block !important;" +
                    "width: 100% !important;" +
                    "height: auto !important;" +
                    "padding: 0 !important;" +
                    "margin: 0 !important;" +
                    "overflow: visible !important;" +
                    "background: #ffffff !important;" +
                    "}" +

                    ".VAS-glje-print-dialog " +
                    ".VAS-glje-detail-card {" +
                    "position: static !important;" +
                    "display: block !important;" +
                    "transform: none !important;" +
                    "inset: auto !important;" +
                    "width: 100% !important;" +
                    "max-width: none !important;" +
                    "height: auto !important;" +
                    "max-height: none !important;" +
                    "margin: 0 !important;" +
                    "overflow: visible !important;" +
                    "box-shadow: none !important;" +
                    "border-radius: 0 !important;" +
                    "}" +

                    ".VAS-glje-print-dialog " +
                    ".VAS-glje-detail-body," +

                    ".VAS-glje-print-dialog " +
                    ".VAS-glje-detail-content," +

                    ".VAS-glje-print-dialog " +
                    ".VAS-glje-detail-lines-wrap {" +
                    "display: block !important;" +
                    "height: auto !important;" +
                    "max-height: none !important;" +
                    "overflow: visible !important;" +
                    "}" +

                    ".VAS-glje-print-dialog " +
                    ".VAS-glje-detail-lines {" +
                    "width: 100% !important;" +
                    "table-layout: auto !important;" +
                    "}" +

                    ".VAS-glje-print-dialog " +
                    ".VAS-glje-dialog-footer," +

                    ".VAS-glje-print-dialog " +
                    ".VAS-glje-dialog-close," +

                    ".VAS-glje-print-dialog " +
                    ".VAS-glje-dialog-busy {" +
                    "display: none !important;" +
                    "}" +

                    "thead {" +
                    "display: table-header-group !important;" +
                    "}" +

                    "tfoot {" +
                    "display: table-footer-group !important;" +
                    "}" +

                    "tr," +
                    ".VAS-glje-detail-summary > div," +
                    ".VAS-glje-created-strip {" +
                    "break-inside: avoid !important;" +
                    "page-break-inside: avoid !important;" +
                    "}" +

                    "@media print {" +

                    "html," +
                    "body {" +
                    "-webkit-print-color-adjust: exact !important;" +
                    "print-color-adjust: exact !important;" +
                    "}" +

                    "}" +

                    "</style>" +
                    "</head>" +

                    "<body>" +

                    '<div class="VAS-glje-dialog ' +
                    'VAS-glje-detail-dialog ' +
                    'VAS-glje-print-dialog">' +

                    $popupCopy[0].outerHTML +

                    "</div>" +

                    "</body>" +
                    "</html>"
                );

                printDocument.close();

                var cleaned = false;
                var printStarted = false;

                /*
                 * Additional time after HTML, CSS,
                 * images and fonts are loaded.
                 */
                var printRenderDelay = 2000;
                var maximumLoadWait = 10000;

                function cleanupPrintFrame() {
                    if (cleaned) {
                        return;
                    }

                    cleaned = true;

                    window.setTimeout(
                        function () {
                            if (
                                printFrame &&
                                printFrame.parentNode
                            ) {
                                printFrame
                                    .parentNode
                                    .removeChild(
                                        printFrame
                                    );
                            }
                        },
                        500
                    );
                }

                function arePrintImagesLoaded() {
                    var images =
                        printDocument.images || [];

                    for (
                        var index = 0;
                        index < images.length;
                        index++
                    ) {
                        if (!images[index].complete) {
                            return false;
                        }
                    }

                    return true;
                }

                function startBrowserPrint() {
                    if (printStarted) {
                        return;
                    }

                    printStarted = true;

                    window.setTimeout(
                        function () {
                            try {
                                printWindow.focus();
                                printWindow.print();
                            }
                            catch (error) {
                                cleanupPrintFrame();

                                showProcessError(
                                    lbl(
                                        "VAS_044_PrintWindowFailed",
                                        "Could not open the print window."
                                    )
                                );
                            }

                            window.setTimeout(
                                cleanupPrintFrame,
                                60000
                            );
                        },
                        printRenderDelay
                    );
                }

                function waitForPrintContent() {
                    var waitStartedAt =
                        new Date().getTime();

                    function checkContent() {
                        var currentTime =
                            new Date().getTime();

                        var reachedMaximumWait =
                            currentTime -
                            waitStartedAt >=
                            maximumLoadWait;

                        var htmlReady =
                            printDocument.readyState ===
                            "complete";

                        var imagesReady =
                            arePrintImagesLoaded();

                        if (
                            (
                                htmlReady &&
                                imagesReady
                            ) ||
                            reachedMaximumWait
                        ) {
                            if (
                                printDocument.fonts &&
                                printDocument.fonts.ready
                            ) {
                                printDocument.fonts.ready
                                    .then(
                                        startBrowserPrint
                                    )
                                    .catch(
                                        startBrowserPrint
                                    );
                            }
                            else {
                                startBrowserPrint();
                            }

                            return;
                        }

                        window.setTimeout(
                            checkContent,
                            100
                        );
                    }

                    checkContent();
                }

                printWindow.onafterprint =
                    cleanupPrintFrame;

                waitForPrintContent();
            }

            function exportDialogRows() {
                var $table =
                    $dialogBody.find(
                        ".VAS-glje-dialog-table"
                    );

                if (!$table.length) {
                    return;
                }

                var excelHtml =
                    '<html xmlns:o="urn:schemas-microsoft-com:office:office"' +
                    ' xmlns:x="urn:schemas-microsoft-com:office:excel"' +
                    ' xmlns="http://www.w3.org/TR/REC-html40">' +

                    '<head><meta charset="utf-8"></head>' +

                    "<body>" +

                    $table[0].outerHTML +

                    "</body>" +
                    "</html>";

                var blob =
                    new Blob(
                        [excelHtml],
                        {
                            type:
                                "application/vnd.ms-excel;charset=utf-8;"
                        }
                    );

                var objectUrl =
                    URL.createObjectURL(
                        blob
                    );

                var downloadLink =
                    document.createElement(
                        "a"
                    );

                downloadLink.href =
                    objectUrl;

                downloadLink.download =
                    "gl-journal-entries.xls";

                document.body.appendChild(
                    downloadLink
                );

                downloadLink.click();

                document.body.removeChild(
                    downloadLink
                );

                URL.revokeObjectURL(
                    objectUrl
                );
            }

            function initials(name) {
                var parts =
                    String(
                        name || ""
                    )
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

            function showProcessError(
                message
            ) {
                window.alert(
                    message ||
                    lbl(
                        "VAS_044_JournalProcessFailed",
                        "Journal process failed."
                    )
                );
            }

            function renderDetailError(
                message
            ) {
                detailLoaded = false;

                selectedJournalStatus = "";
                selectedJournalPosted = false;

                $detailBody.html(
                    '<div class="VAS-glje-dialog-empty">' +
                    esc(
                        message ||
                        lbl(
                            "VIS_Error",
                            "Error loading data."
                        )
                    ) +
                    "</div>"
                );

                updateActionButtons();
            }

            function renderDialogError(
                message
            ) {
                dialogLoaded = false;

                $dialogBody.html(
                    '<div class="VAS-glje-dialog-empty">' +
                    esc(
                        message ||
                        lbl(
                            "VIS_Error",
                            "Error loading data."
                        )
                    ) +
                    "</div>"
                );

                $dialogFooterText.text("");
            }

            function refreshData() {
                dialogLoaded = false;

                $kpiValue.html(
                    "&mdash;"
                );

                $whyText.html(
                    "&mdash;"
                );

                loadData();

                if (
                    $dialog &&
                    $dialog.is(
                        ":visible"
                    )
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

                        refreshTimer = null;
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
                        "keydown.VAS-glje-" +
                        $self.AD_UserHomeWidgetID
                    );

                    $("body").removeClass(
                        "VAS-glje-body-lock"
                    );

                    if ($detailDialog) {
                        $detailDialog.remove();
                        $detailDialog = null;
                    }

                    if ($dialog) {
                        $dialog.remove();
                        $dialog = null;
                    }

                    $root.off();
                    $root.remove();

                    $kpiValue = null;
                    $whyText = null;
                    $titleElement = null;

                    $dialogBody = null;
                    $dialogFooterText = null;
                    $dialogBusy = null;

                    $detailBody = null;
                    $detailBusy = null;

                    $approveButton = null;
                    $postButton = null;
                    $downloadButton = null;
                    $detailCloseButton = null;

                    selectedJournalId = 0;
                    selectedJournalStatus = "";
                    selectedJournalPosted = false;

                    dialogLoaded = false;
                    detailLoaded = false;
                };
        };

    VAS.VAS_041_GLJournalEntriesWidget
        .prototype.refreshWidget =
        function () {
            if (
                typeof this.refreshData ===
                "function"
            ) {
                this.refreshData();
            }
        };

    VAS.VAS_041_GLJournalEntriesWidget
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

    VAS.VAS_041_GLJournalEntriesWidget
        .prototype.widgetSizeChange =
        function (
            height,
            width
        ) {
        };

    VAS.VAS_041_GLJournalEntriesWidget
        .prototype.dispose =
        function () {
            this.disposeComponent();

            if (this.frame) {
                this.frame.dispose();
            }

            this.frame = null;
        };

})(VAS, jQuery);

