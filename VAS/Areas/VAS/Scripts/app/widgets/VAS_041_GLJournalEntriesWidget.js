/**
 * GL Journal Entries KPI Widget
 * Purpose - Display monthly GL journal entries, details, approval, posting, export, and PDF actions.
 *
 * -- Labels / Message Keys --------------------------------------------
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Entries                              | VAS_041_GLJEntries
 *  2  | All Journal Entries                  | VAS_041_AllJournalEntries
 *  3  | This Month                           | VAS_041_ThisMonth
 *  4  | Close                                | VAS_Close
 *  5  | Export                               | VAS_Export
 *  6  | Download PDF                         | VAS_DownloadPDF
 *  7  | Approve                              | VAS_041_Approve
 *  8  | Post Journal                         | VAS_041_PostJournal
 *  9  | Error Loading Data                   | VIS_Error
 * 10  | No Data                              | VIS_NoData
 * 11  | Journal No.                          | VAS_041_JournalNo
 * 12  | Date                                 | VAS_041_Date
 * 13  | Description                          | VAS_041_Description
 * 14  | Status                               | VAS_041_Status
 * 15  | Total Debit                          | VAS_041_TotalDebit
 * 16  | Total Credit                         | VAS_041_TotalCredit
 * 17  | No Journal Lines                     | VAS_041_NoJournalLines
 * 18  | Journal Process Failed               | VAS_041_JournalProcessFailed
 * 19  | Approving                            | VAS_041_Approving
 * 20  | Posting                              | VAS_041_Posting
 * 21  | Details Not Loaded                   | VAS_041_DetailsNotLoaded
 * 22  | Details Not Available                | VAS_041_DetailsNotAvailable
 * 23  | Could Not Open Print Window          | VAS_041_PrintWindowFailed
 * ---------------------------------------------------------------------
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

    function bindGLJournalOverflowTitle() {
        if (window.VASGLJournalOverflowTooltipBound) {
            return;
        }

        window.VASGLJournalOverflowTooltipBound = true;

        var selector =
            '[class*="VAS-glj"] td,' +
            '[class*="VAS-glj"] th,' +
            '[class*="VAS-glj"] strong,' +
            '[class*="VAS-glj"] .w-title,' +
            '[class*="VAS-glj"] [class*="-title"],' +
            '[class*="VAS-glj"] [class*="-desc"],' +
            '[class*="VAS-glj"] [class*="-meta"],' +
            '[class*="VAS-glj"] [class*="-sub"],' +
            '[class*="VAS-glj"] [class*="-name"]';

        function isClipped(element) {
            return (
                element.scrollWidth >
                    element.clientWidth + 1 ||
                element.scrollHeight >
                    element.clientHeight + 1
            );
        }

        function cleanText(text) {
            return $.trim(
                String(
                    text || ""
                ).replace(/\s+/g, " ")
            );
        }

        $(document)
            .on(
                "mouseenter",
                selector,
                function () {
                    var text =
                        cleanText(
                            $(this).text()
                        );

                    if (
                        !text ||
                        text.length < 2 ||
                        !isClipped(this)
                    ) {
                        $(this).removeAttr(
                            "title"
                        );
                        return;
                    }

                    $(this).attr(
                        "title",
                        text
                    );
                }
            )
            .on(
                "mouseleave",
                selector,
                function () {
                    $(this).removeAttr(
                        "title"
                    );
                }
            )
            .on(
                "focusin",
                selector,
                function () {
                    var text =
                        cleanText(
                            $(this).text()
                        );

                    if (
                        text &&
                        text.length > 1 &&
                        isClipped(this)
                    ) {
                        $(this).attr(
                            "title",
                            text
                        );
                    }
                }
            )
            .on(
                "focusout",
                selector,
                function () {
                    $(this).removeAttr(
                        "title"
                    );
                }
            );
    }

    bindGLJournalOverflowTitle();

    /**
     * Supports:
     * 1. Normal JSON object
     * 2. JSON string
     * 3. ASP.NET response wrapped inside d
     * 4. Double-serialized JSON
     */
    function normalizeResponse(result) {
        var data =
            result;

        if (
            data &&
            data.d !== undefined
        ) {
            data =
                data.d;
        }

        for (
            var index = 0;
            index < 3;
            index++
        ) {
            if (
                typeof data !==
                "string"
            ) {
                break;
            }

            try {
                data =
                    JSON.parse(
                        data
                    );
            }
            catch (error) {
                return null;
            }

            if (
                data &&
                data.d !== undefined
            ) {
                data =
                    data.d;
            }
        }

        return data;
    }

    function formatAmount(
        amount,
        precision
    ) {
        var standardPrecision =
            2;

        try {
            standardPrecision =
                Number(
                    VIS.Env
                        .getCtx()
                        .getStdPrecision()
                );
        }
        catch (error) {
            standardPrecision =
                2;
        }

        var resolvedPrecision =
            Number(
                precision
            );

        if (
            isNaN(
                resolvedPrecision
            ) ||
            resolvedPrecision < 0
        ) {
            resolvedPrecision =
                standardPrecision;
        }

        if (
            isNaN(
                resolvedPrecision
            ) ||
            resolvedPrecision < 0
        ) {
            resolvedPrecision =
                2;
        }

        var numericAmount =
            Number(
                amount || 0
            );

        if (
            isNaN(
                numericAmount
            )
        ) {
            numericAmount =
                0;
        }

        return numericAmount
            .toLocaleString(
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

    function getAjaxErrorMessage(
        xhr,
        fallback
    ) {
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
                fallback ||
                "Error loading data."
            );
        }

        if (
            xhr &&
            xhr.status
        ) {
            return (
                "Request failed. HTTP " +
                xhr.status
            );
        }

        return (
            fallback ||
            "Error loading data."
        );
    }

    var PILL_CLASS = {
        "DR":
            "VAS-glje-pill-draft",

        "CO":
            "VAS-glje-pill-posted",

        "CL":
            "VAS-glje-pill-posted",

        "IP":
            "VAS-glje-pill-submit",

        "AP":
            "VAS-glje-pill-posted",

        "NA":
            "VAS-glje-pill-pending",

        "VO":
            "VAS-glje-pill-voided",

        "RE":
            "VAS-glje-pill-returned"
    };

    VAS.VAS_041_GLJournalEntriesWidget =
        function () {
            this.frame =
                null;

            this.windowNo =
                0;

            this.AD_UserHomeWidgetID =
                0;

            var $self =
                this;

            var $root =
                $(
                    '<div class="VAS-glje-root">'
                );

            var $kpiValue =
                null;

            var $whyText =
                null;

            var $titleElement =
                null;

            var $dialog =
                null;

            var $dialogBody =
                null;

            var $dialogFooterText =
                null;

            var $dialogBusy =
                null;

            var $detailDialog =
                null;

            var $detailBody =
                null;

            var $detailBusy =
                null;

            var $approveButton =
                null;

            var $postButton =
                null;

            var $downloadButton =
                null;

            var $detailCloseButton =
                null;

            var dialogLoaded =
                false;

            var detailLoaded =
                false;

            var dialogPageNo =
                1;

            var dialogPageSize =
                10;

            var dialogTotalPages =
                1;

            var detailLinePageNo =
                1;

            var detailLinePageSize =
                3;

            var detailLineTotalPages =
                1;

            var selectedJournalId =
                0;

            var selectedJournalStatus =
                "";

            var selectedJournalPosted =
                false;

            var journalActionInProgress =
                false;

            var refreshTimer =
                null;

            var countRequest =
                null;

            var listRequest =
                null;

            var detailRequest =
                null;

            var actionRequest =
                null;

            var isDisposed =
                false;

            var baseUrl =
                VIS.Application.contextUrl;

            this.Initalize =
                function () {
                    createWidget();
                    createBusyIndicator();

                    showBusy(
                        true
                    );

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

                $root.append(
                    $busy
                );
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
                    'role="button" ' +
                    'tabindex="0">' +

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

                $root.append(
                    html
                );

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
                            event.key ===
                            "Enter" ||
                            event.key ===
                            " "
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

                showBusy(
                    true
                );

                countRequest =
                    $.ajax({
                        url:
                            baseUrl +
                            "VAS/VAS_041_GLJournalEntriesWidget/GetMonthlyEntryCount",

                        type:
                            "GET",

                        dataType:
                            "json",

                        cache:
                            false,

                        success:
                            function (result) {
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
                                        : Number(
                                            data.EntryCount ||
                                            0
                                        )
                                );

                                $whyText.text(
                                    lbl(
                                        "VAS_041_AllJournalEntries",
                                        "All journal entries created in"
                                    ) +

                                    " " +

                                    (
                                        data.MonthName ||
                                        ""
                                    ) +

                                    "."
                                );
                            },

                        error:
                            function (
                                xhr,
                                textStatus
                            ) {
                                if (
                                    isDisposed ||
                                    textStatus ===
                                    "abort"
                                ) {
                                    return;
                                }

                                $kpiValue.html(
                                    "&mdash;"
                                );

                                $whyText.text(
                                    getAjaxErrorMessage(
                                        xhr,
                                        lbl(
                                            "VIS_Error",
                                            "Error loading data."
                                        )
                                    )
                                );
                            },

                        complete:
                            function () {
                                countRequest =
                                    null;

                                if (!isDisposed) {
                                    showBusy(
                                        false
                                    );
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
                        esc(
                            title
                        ) +
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

                showDialogBusy(
                    false
                );

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

                /*
                 * Delegated event.
                 *
                 * This event remains active even after
                 * $dialogBody.html(...) rebuilds the table.
                 */
                $dialogBody
                    .off(
                        "click.VAS041JournalDetail",
                        ".VAS-glje-entry-row"
                    )
                    .on(
                        "click.VAS041JournalDetail",
                        ".VAS-glje-entry-row",
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
                                isNaN(
                                    journalId
                                ) ||
                                journalId <= 0
                            ) {
                                renderDetailError(
                                    "Invalid journal ID."
                                );

                                return;
                            }

                            openDetailDialog(
                                journalId
                            );
                        }
                    );

                /*
                 * Keyboard support on the rows.
                 */
                $dialogBody
                    .off(
                        "keydown.VAS041JournalDetail",
                        ".VAS-glje-entry-row"
                    )
                    .on(
                        "keydown.VAS041JournalDetail",
                        ".VAS-glje-entry-row",
                        function (event) {
                            if (
                                event.key !==
                                "Enter" &&
                                event.key !==
                                " "
                            ) {
                                return;
                            }

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
                                isNaN(
                                    journalId
                                ) ||
                                journalId <= 0
                            ) {
                                return;
                            }

                            openDetailDialog(
                                journalId
                            );
                        }
                    );

                $dialogBody
                    .parent()
                    .off(
                        "click.VAS041DialogPager",
                        ".VAS-glje-dialog-page"
                    )
                    .on(
                        "click.VAS041DialogPager",
                        ".VAS-glje-dialog-page",
                        function (event) {
                            event.preventDefault();
                            event.stopPropagation();

                            var nextPage =
                                $(this).hasClass(
                                    "VAS-glje-dialog-prev"
                                )
                                    ? dialogPageNo - 1
                                    : dialogPageNo + 1;

                            if (
                                nextPage < 1 ||
                                nextPage === dialogPageNo ||
                                nextPage > dialogTotalPages
                            ) {
                                return;
                            }

                            dialogPageNo =
                                nextPage;

                            dialogLoaded =
                                false;

                            loadDialogRows();
                        }
                    );

                $(document).on(
                    "keydown.VAS-glje-" +
                    id,
                    function (event) {
                        if (
                            event.key ===
                            "Escape" &&
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
                        '" style="display:none;z-index:1000002" ' +
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

                showDetailBusy(
                    false
                );

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
                if (
                    !$dialog ||
                    isDisposed
                ) {
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
                    !$detailDialog.length ||
                    isNaN(
                        journalId
                    ) ||
                    journalId <= 0
                ) {
                    return;
                }

                selectedJournalId =
                    journalId;

                selectedJournalStatus =
                    "";

                selectedJournalPosted =
                    false;

                detailLoaded =
                    false;

                $detailBody.empty();

                $detailDialog.find(
                    "#VAS-glje-detail-title-" +
                    $self.AD_UserHomeWidgetID
                ).html(
                    "&mdash;"
                );

                $detailDialog.find(
                    "#VAS-glje-detail-sub-" +
                    $self.AD_UserHomeWidgetID
                ).html(
                    "&mdash;"
                );

                updateActionButtons();

                /*
                 * Ensure that detail popup appears
                 * above the list popup.
                 */
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

                selectedJournalId =
                    0;

                selectedJournalStatus =
                    "";

                selectedJournalPosted =
                    false;

                detailLoaded =
                    false;

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

                showDialogBusy(
                    true
                );

                listRequest =
                    $.ajax({
                        url:
                            baseUrl +
                            "VAS/VAS_041_GLJournalEntriesWidget/GetMonthlyEntries",

                        type:
                            "GET",

                        data:
                            {
                                pageNo:
                                    dialogPageNo,

                                pageSize:
                                    dialogPageSize
                            },

                        dataType:
                            "json",

                        cache:
                            false,

                        success:
                            function (result) {
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

                                    dialogLoaded =
                                        true;
                                }
                                else {
                                    renderDialogError(
                                        data &&
                                        (
                                            data.errorText ||
                                            data.error ||
                                            data.message
                                        )
                                    );
                                }
                            },

                        error:
                            function (
                                xhr,
                                textStatus
                            ) {
                                if (
                                    isDisposed ||
                                    textStatus ===
                                    "abort"
                                ) {
                                    return;
                                }

                                renderDialogError(
                                    getAjaxErrorMessage(
                                        xhr,
                                        "Error loading journal entries."
                                    )
                                );
                            },

                        complete:
                            function () {
                                listRequest =
                                    null;

                                if (!isDisposed) {
                                    showDialogBusy(
                                        false
                                    );
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
                    data.MonthName ||
                    "";

                var year =
                    data.Year ||
                    "";

                dialogPageNo =
                    Number(
                        data.PageNo ||
                        dialogPageNo ||
                        1
                    );

                dialogPageSize =
                    Number(
                        data.PageSize ||
                        dialogPageSize ||
                        10
                    );

                dialogTotalPages =
                    Math.max(
                        1,
                        Number(
                            data.TotalPages ||
                            Math.ceil(
                                Number(
                                    data.TotalCount ||
                                    rows.length ||
                                    0
                                ) /
                                dialogPageSize
                            ) ||
                            1
                        )
                    );

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

                    $dialogFooterText.text(
                        ""
                    );

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
                            "VAS_041_Date",
                            "Date"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_041_Description",
                            "Description"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_041_Status",
                            "Status"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_041_TotalDebit",
                            "Total Debit"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
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
                        rows[index] || {};

                    var journalId =
                        parseInt(
                            row.GL_Journal_ID,
                            10
                        );

                    if (
                        isNaN(
                            journalId
                        ) ||
                        journalId <= 0
                    ) {
                        continue;
                    }

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
                        formatAmount(
                            row.TotalDebit,
                            precision
                        );

                    var credit =
                        formatAmount(
                            row.TotalCredit,
                            precision
                        );

                    html +=
                        '<tr class="VAS-glje-entry-row" ' +

                        'role="button" ' +

                        'tabindex="0" ' +

                        'data-journal-id="' +
                        journalId +
                        '" ' +

                        'title="' +
                        esc(
                            (
                                row.DocumentNo ||
                                ""
                            ) +

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

                        '<td class="VAS-glje-amt" title="' + esc(debit) + '">' +
                        esc(debit) +
                        "</td>" +

                        '<td class="VAS-glje-amt" title="' + esc(credit) + '">' +
                        esc(credit) +
                        "</td>" +

                        "</tr>";
                }

                html +=
                    "</tbody>" +
                    "</table>";

                /*
                 * Event is delegated in createDialog.
                 * Do not bind .click() here.
                 */
                $dialogBody.html(
                    html
                );

                $dialogBody
                    .siblings(
                        ".VAS-glje-dialog-pager"
                    )
                    .remove();

                $dialogBody.after(
                    renderDialogPager()
                );

                $dialogFooterText.text(
                    rows.length +

                    " journals \u00B7 total " +

                    symbol +

                    formatAmount(
                        data.TotalDebit,
                        precision
                    )
                );
            }

            function renderDialogPager() {
                var html =
                    '<div class="VAS-glje-line-pager VAS-glje-dialog-pager">';

                html +=
                    '<button type="button" ' +
                    'class="VAS-glje-page-btn VAS-glje-dialog-page VAS-glje-dialog-prev" ' +
                    (
                        dialogPageNo <= 1 ||
                        dialogTotalPages <= 1
                            ? "disabled "
                            : ""
                    ) +
                    'aria-label="' +
                    esc(lbl("VIS_Previous", "Previous")) +
                    '">&#8249;</button>' +

                    '<span class="VAS-glje-page-text">' +
                    esc(
                        dialogPageNo +
                        " " +
                        lbl("VIS_Of", "of") +
                        " " +
                        dialogTotalPages
                    ) +
                    "</span>" +

                    '<button type="button" ' +
                    'class="VAS-glje-page-btn VAS-glje-dialog-page VAS-glje-dialog-next" ' +
                    (
                        dialogPageNo >= dialogTotalPages ||
                        dialogTotalPages <= 1
                            ? "disabled "
                            : ""
                    ) +
                    'aria-label="' +
                    esc(lbl("VIS_Next", "Next")) +
                    '">&#8250;</button>';

                html +=
                    "</div>";

                return html;
            }

            function loadJournalDetail(
                journalId
            ) {
                journalId =
                    parseInt(
                        journalId,
                        10
                    );

                if (
                    isNaN(
                        journalId
                    ) ||
                    journalId <= 0
                ) {
                    renderDetailError(
                        "Invalid journal ID."
                    );

                    return;
                }

                if (
                    detailRequest &&
                    detailRequest.readyState !== 4
                ) {
                    detailRequest.abort();
                }

                detailLoaded =
                    false;

                updateActionButtons();

                showDetailBusy(
                    true
                );

                $detailBody.empty();

                $detailBody
                    .off(
                        ".VAS041LinePager"
                    )
                    .on(
                        "click.VAS041LinePager",
                        ".VAS-glje-line-prev, .VAS-glje-line-next",
                        function () {
                            if (
                                journalActionInProgress ||
                                selectedJournalId <= 0
                            ) {
                                return;
                            }

                            var nextPage =
                                $(this).hasClass(
                                    "VAS-glje-line-prev"
                                )
                                    ? detailLinePageNo - 1
                                    : detailLinePageNo + 1;

                            if (
                                nextPage < 1 ||
                                nextPage > detailLineTotalPages
                            ) {
                                return;
                            }

                            detailLinePageNo =
                                nextPage;

                            loadJournalDetail(
                                selectedJournalId
                            );
                        }
                    );

                detailRequest =
                    $.ajax({
                        url:
                            baseUrl +
                            "VAS/VAS_041_GLJournalEntriesWidget/GetJournalEntryDetail",

                        type:
                            "GET",

                        dataType:
                            "json",

                        cache:
                            false,

                        data: {
                            journalId:
                                journalId,

                            pageNo:
                                detailLinePageNo,

                            pageSize:
                                detailLinePageSize
                        },

                        success:
                            function (result) {
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
                                    data.success !== false &&
                                    data.Journal
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
                                                data.error ||
                                                data.message
                                            )
                                            ? (
                                                data.errorText ||
                                                data.error ||
                                                data.message
                                            )
                                            : "Journal details not found."
                                    );
                                }
                            },

                        error:
                            function (
                                xhr,
                                textStatus,
                                errorThrown
                            ) {
                                if (
                                    isDisposed ||
                                    textStatus ===
                                    "abort"
                                ) {
                                    return;
                                }

                                var message =
                                    getAjaxErrorMessage(
                                        xhr,
                                        errorThrown ||
                                        "Error loading journal details."
                                    );

                                /*
                                 * Shows the real server response
                                 * inside browser developer tools.
                                 */
                                if (
                                    window.console &&
                                    console.error
                                ) {
                                    console.error(
                                        "GetJournalEntryDetail failed:",
                                        {
                                            journalId:
                                                journalId,

                                            status:
                                                xhr
                                                    ? xhr.status
                                                    : 0,

                                            responseText:
                                                xhr
                                                    ? xhr.responseText
                                                    : "",

                                            error:
                                                errorThrown
                                        }
                                    );
                                }

                                renderDetailError(
                                    message
                                );
                            },

                        complete:
                            function () {
                                detailRequest =
                                    null;

                                if (!isDisposed) {
                                    showDetailBusy(
                                        false
                                    );
                                }
                            }
                    });
            }

            function renderJournalDetail(
                data
            ) {
                var journal =
                    data.Journal ||
                    {};

                var lines =
                    Array.isArray(
                        data.Lines
                    )
                        ? data.Lines
                        : [];

                detailLinePageNo =
                    Number(
                        data.LinePageNo ||
                        detailLinePageNo ||
                        1
                    );

                detailLinePageSize =
                    Number(
                        data.LinePageSize ||
                        detailLinePageSize ||
                        3
                    );

                detailLineTotalPages =
                    Math.max(
                        Number(
                            data.LineTotalPages ||
                            1
                        ),
                        1
                    );

                var detailLineCount =
                    Number(
                        data.LineCount ||
                        lines.length ||
                        0
                    );

                var symbol =
                    data.CurSymbol ||
                    data.ISOCode ||
                    "";

                var precision =
                    Number(
                        data.StdPrecision
                    );

                var returnedJournalId =
                    parseInt(
                        journal.GL_Journal_ID,
                        10
                    );

                if (
                    !isNaN(
                        returnedJournalId
                    ) &&
                    returnedJournalId > 0
                ) {
                    selectedJournalId =
                        returnedJournalId;
                }

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

                    " \u00B7 " +

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

                    " \u00B7 " +

                    (
                        journal.DateAcct ||
                        ""
                    )
                );

                var totalDebitAmt =
                    formatAmount(
                        journal.TotalDebit,
                        precision
                    );

                var totalCreditAmt =
                    formatAmount(
                        journal.TotalCredit,
                        precision
                    );

                var totalDebit = symbol + totalDebitAmt;
                var totalCredit = symbol + totalCreditAmt;

                var accountingBook =
                    journal.AccountingBook ||
                    "Primary";

                var currencyText =
                    symbol;

                if (
                    data.ISOCode &&
                    data.ISOCode !== symbol
                ) {
                    currencyText +=
                        (
                            currencyText
                                ? " \u00B7 "
                                : ""
                        ) +
                        data.ISOCode;
                }

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
                    "<span>Currency</span>" +
                    "<strong>" +
                    esc(
                        currencyText
                    ) +
                    "</strong>" +
                    "</div>" +

                    "<div>" +

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
                                "VAS_041_NoJournalLines",
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
                            lines[index] ||
                            {};

                        var accountText =
                            "";

                        if (
                            line.AccountCode &&
                            line.AccountName
                        ) {
                            accountText =
                                line.AccountCode +

                                " \u00B7 " +

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

                            (function () {
                                var ld = Number(line.Debit || 0) > 0 ? formatAmount(line.Debit, precision) : "-";
                                var lc = Number(line.Credit || 0) > 0 ? formatAmount(line.Credit, precision) : "-";
                                return '<td class="VAS-glje-amt" title="' + esc(ld) + '">' + esc(ld) + "</td>" +
                                       '<td class="VAS-glje-amt" title="' + esc(lc) + '">' + esc(lc) + "</td>";
                            }()) +

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

                    '<td class="VAS-glje-amt" title="' + esc(totalDebitAmt) + '">' +
                    esc(totalDebitAmt) +
                    "</td>" +

                    '<td class="VAS-glje-amt" title="' + esc(totalCreditAmt) + '">' +
                    esc(totalCreditAmt) +
                    "</td>" +

                    '<td colspan="4"></td>' +

                    "</tr>" +
                    "</tfoot>" +

                    "</table>" +
                    "</div>" +

                    '<div class="VAS-glje-line-pager">' +

                    '<button type="button" ' +
                    'class="VAS-glje-page-btn VAS-glje-line-prev" ' +
                    (
                        detailLinePageNo <= 1 ||
                        detailLineTotalPages <= 1
                            ? "disabled "
                            : ""
                    ) +
                    'aria-label="Previous">&#8249;</button>' +

                    '<span class="VAS-glje-page-text">' +
                    esc(
                        detailLineCount
                            ? (
                                detailLinePageNo +
                                " of " +
                                detailLineTotalPages
                            )
                            : ""
                    ) +
                    "</span>" +

                    '<button type="button" ' +
                    'class="VAS-glje-page-btn VAS-glje-line-next" ' +
                    (
                        detailLinePageNo >= detailLineTotalPages ||
                        detailLineTotalPages <= 1
                            ? "disabled "
                            : ""
                    ) +
                    'aria-label="Next">&#8250;</button>' +

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
                                " \u00B7 drafted " +

                                esc(
                                    journal.CreatedDate
                                )
                            )
                            : ""
                    ) +

                    "</div>" +
                    "</div>";

                $detailBody.html(
                    html
                );

                detailLoaded =
                    true;

                updateActionButtons();
            }

            function updateActionButtons() {
                var status =
                    String(
                        selectedJournalStatus ||
                        ""
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
                        .toggle(
                            canApprove
                        )
                        .prop(
                            "disabled",
                            journalActionInProgress ||
                            !canApprove ||
                            selectedJournalId <= 0
                        );
                }

                if ($postButton) {
                    $postButton
                        .toggle(
                            canPost
                        )
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
                    selectedJournalId <= 0 ||
                    !detailLoaded
                ) {
                    return;
                }

                var status =
                    String(
                        selectedJournalStatus ||
                        ""
                    ).toUpperCase();

                if (
                    actionType ===
                    "approve" &&
                    status !== "DR" &&
                    status !== "IP" &&
                    status !== "NA"
                ) {
                    return;
                }

                if (
                    actionType ===
                    "post" &&
                    status !== "AP" &&
                    status !== "CO" &&
                    status !== "CL"
                ) {
                    return;
                }

                journalActionInProgress =
                    true;

                setActionBusy(
                    true,
                    actionType
                );

                actionRequest =
                    $.ajax({
                        url:
                            baseUrl +
                            "VAS/VAS_041_GLJournalEntriesWidget/" +
                            actionName,

                        type:
                            "POST",

                        dataType:
                            "json",

                        cache:
                            false,

                        data: {
                            journalId:
                                selectedJournalId
                        },

                        success:
                            function (result) {
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
                                            "VAS_041_JournalProcessFailed",
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

                                /*
                                 * Refresh all displayed data.
                                 */
                                dialogLoaded =
                                    false;

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

                        error:
                            function (
                                xhr,
                                textStatus
                            ) {
                                if (
                                    textStatus ===
                                    "abort"
                                ) {
                                    return;
                                }

                                showProcessError(
                                    getAjaxErrorMessage(
                                        xhr,
                                        lbl(
                                            "VAS_041_JournalProcessFailed",
                                            "Journal process failed."
                                        )
                                    )
                                );
                            },

                        complete:
                            function () {
                                actionRequest =
                                    null;

                                journalActionInProgress =
                                    false;

                                setActionBusy(
                                    false,
                                    actionType
                                );
                            }
                    });
            }

            function setActionBusy(
                busy,
                actionType
            ) {
                showDetailBusy(
                    busy
                );

                if ($approveButton) {
                    $approveButton.text(
                        busy &&
                            actionType ===
                            "approve"
                            ? lbl(
                                "VAS_041_Approving",
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
                            actionType ===
                            "post"
                            ? lbl(
                                "VAS_041_Posting",
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
                            "VAS_041_DetailsNotLoaded",
                            "Journal details are not loaded."
                        )
                    );

                    return;
                }

                journalActionInProgress =
                    true;

                showDetailBusy(
                    true
                );

                updateActionButtons();

                $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_041_GLJournalEntriesWidget/" +
                        "GetJournalPrintInfo",

                    type:
                        "GET",

                    dataType:
                        "json",

                    cache:
                        false,

                    data: {
                        journalId:
                            selectedJournalId
                    },

                    success:
                        function (rawInfo) {
                            var info =
                                normalizeResponse(
                                    rawInfo
                                );

                            var processId =
                                Number(
                                    info &&
                                    (
                                        info.AD_Process_ID ||
                                        info.ad_Process_ID ||
                                        info.adProcessId
                                    )
                                ) || 0;

                            var tableId =
                                Number(
                                    info &&
                                    (
                                        info.AD_Table_ID ||
                                        info.ad_Table_ID ||
                                        info.adTableId
                                    )
                                ) || 0;

                            if (
                                processId <= 0 ||
                                tableId <= 0
                            ) {
                                finishPdfDownload();

                                showProcessError(
                                    lbl(
                                        "VAS_041_PrintProcessNotFound",
                                        "Print process is not configured for GL Journal."
                                    )
                                );

                                return;
                            }

                            generateJournalPDF(
                                processId,
                                tableId
                            );
                        },

                    error:
                        function (xhr) {
                            finishPdfDownload();

                            showProcessError(
                                getAjaxErrorMessage(
                                    xhr,
                                    lbl(
                                        "VAS_041_PrintProcessNotFound",
                                        "Print process is not configured for GL Journal."
                                    )
                                )
                            );
                        }
                });
            }

            function generateJournalPDF(
                processId,
                tableId
            ) {

                console.log("tableId => " + tableId + " - processId =>    " + processId
                    + "  - selectedJournalId => " + selectedJournalId);

                $.ajax({
                    url:
                        VIS.Application.contextUrl +
                        "JsonData/GeneratePrint/",

                    dataType:
                        "json",

                    data: {
                        AD_Process_ID:
                            processId,

                        Name:
                            "Print",

                        AD_Table_ID:
                            tableId,

                        Record_ID:
                            selectedJournalId,

                        WindowNo:
                            $self.windowNo || 0,

                        filetype:
                            "P",

                        actionOrigin:
                            "W",

                        originName:
                            "GL Journal"
                    },

                    success:
                        function (raw) {

                            console.log("raw => " + JSON.stringify(raw));
                            var res =
                                normalizeResponse(
                                    raw
                                );

                            var file =
                                res &&
                                (
                                    res.ReportFilePath ||
                                    res.FilePath ||
                                    res.FileName ||
                                    res.fileName ||
                                    res.path
                                );

                            if (!file) {
                                showProcessError(
                                    (
                                        res &&
                                        res.ReportProcessInfo &&
                                        res.ReportProcessInfo.Summary
                                    ) ||
                                    (
                                        res &&
                                        res.ErrorText
                                    ) ||
                                    lbl(
                                        "VAS_041_PrintFailed",
                                        "Could not generate the PDF."
                                    )
                                );

                                return;
                            }

                            window.open(
                                VIS.Application.contextUrl +
                                file,
                                "_blank"
                            );
                        },

                    error:
                        function (xhr) {

                            console.log("raw xhr => " + JSON.stringify(xhr));

                            showProcessError(
                                getAjaxErrorMessage(
                                    xhr,
                                    lbl(
                                        "VAS_041_PrintFailed",
                                        "Could not generate the PDF."
                                    )
                                )
                            );
                        },

                    complete:
                        finishPdfDownload
                });
            }

            function finishPdfDownload() {
                journalActionInProgress =
                    false;

                showDetailBusy(
                    false
                );

                updateActionButtons();
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

                    $table[0]
                        .outerHTML +

                    "</body>" +
                    "</html>";

                var blob =
                    new Blob(
                        [
                            excelHtml
                        ],
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
                        name ||
                        ""
                    )
                        .trim()
                        .split(
                            /\s+/
                        );

                if (
                    !parts.length ||
                    !parts[0]
                ) {
                    return "--";
                }

                if (
                    parts.length ===
                    1
                ) {
                    return parts[0]
                        .charAt(
                            0
                        )
                        .toUpperCase();
                }

                return (
                    parts[0]
                        .charAt(
                            0
                        ) +

                    parts[1]
                        .charAt(
                            0
                        )
                ).toUpperCase();
            }

            function showProcessError(
                message
            ) {
                window.alert(
                    message ||
                    lbl(
                        "VAS_041_JournalProcessFailed",
                        "Journal process failed."
                    )
                );
            }

            function renderDetailError(
                message
            ) {
                detailLoaded =
                    false;

                detailLinePageNo =
                    1;

                detailLineTotalPages =
                    1;

                selectedJournalStatus =
                    "";

                selectedJournalPosted =
                    false;

                $detailBody.html(
                    '<div class="VAS-glje-dialog-empty">' +

                    esc(
                        message ||
                        lbl(
                            "VIS_Error",
                            "Error loading journal details."
                        )
                    ) +

                    "</div>"
                );

                updateActionButtons();
            }

            function renderDialogError(
                message
            ) {
                dialogLoaded =
                    false;

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

                $dialogFooterText.text(
                    ""
                );
            }

            function refreshData() {
                dialogLoaded =
                    false;

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

                if (
                    $detailDialog &&
                    $detailDialog.is(
                        ":visible"
                    ) &&
                    selectedJournalId > 0
                ) {
                    loadJournalDetail(
                        selectedJournalId
                    );
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
                    isDisposed =
                        true;

                    if (refreshTimer) {
                        window.clearInterval(
                            refreshTimer
                        );

                        refreshTimer =
                            null;
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

                    if ($dialogBody) {
                        $dialogBody.off(
                            ".VAS041JournalDetail"
                        );
                    }

                    $("body").removeClass(
                        "VAS-glje-body-lock"
                    );

                    if ($detailDialog) {
                        $detailDialog.remove();

                        $detailDialog =
                            null;
                    }

                    if ($dialog) {
                        $dialog.remove();

                        $dialog =
                            null;
                    }

                    $root.off();

                    $root.remove();

                    $kpiValue =
                        null;

                    $whyText =
                        null;

                    $titleElement =
                        null;

                    $dialogBody =
                        null;

                    $dialogFooterText =
                        null;

                    $dialogBusy =
                        null;

                    $detailBody =
                        null;

                    $detailBusy =
                        null;

                    $approveButton =
                        null;

                    $postButton =
                        null;

                    $downloadButton =
                        null;

                    $detailCloseButton =
                        null;

                    selectedJournalId =
                        0;

                    selectedJournalStatus =
                        "";

                    selectedJournalPosted =
                        false;

                    dialogLoaded =
                        false;

                    detailLoaded =
                        false;
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
            this.frame =
                frame;

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

            this.frame =
                null;
        };

})(VAS, jQuery);
