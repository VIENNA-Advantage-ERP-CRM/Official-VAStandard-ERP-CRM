
/**
 * GL Journal Pending Action Queue Widget
 * Purpose - Displays GL journals awaiting user action.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                               | Message Key
 * ----+--------------------------------------------+-------------------------------
 *  1  | Pending Action Queue                       | VAS_045_PendingActionQueue
 *  2  | items                                      | VAS_045_Items
 *  3  | overdue                                    | VAS_045_Overdue
 *  4  | Previous                                   | VIS_Previous
 *  5  | Next                                       | VIS_Next
 *  6  | Close                                      | VAS_Close
 *  7  | Approve                                    | VAS_041_Approve
 *  8  | Post journal                               | VAS_041_PostJournal
 *  9  | Error loading data.                        | VIS_Error
 * 10  | No pending journals.                       | VIS_NoData
 * 11  | Journal details not found                  | VAS_045_JournalDetailsNotFound
 * 12  | Journal object was not returned from server| VAS_045_JournalObjectNotReturned
 * 13  | No journal lines were returned from server | VAS_045_NoJournalLinesReturned
 * 14  | Journal Lines                              | VAS_045_JournalLines
 * 15  | Journal No.                                | VAS_045_JournalNo
 * 16  | Date                                       | VAS_045_Date
 * 17  | Status                                     | VAS_045_Status
 * 18  | Accounting Book                            | VAS_045_AccountingBook
 * 19  | Total Debit                                | VAS_045_TotalDebit
 * 20  | Total Credit                               | VAS_045_TotalCredit
 * 21  | Description                                | VAS_045_Description
 * 22  | Created By                                 | VAS_045_CreatedBy
 * 23  | Account                                    | VAS_045_Account
 * 24  | Debit                                      | VAS_045_Debit
 * 25  | Credit                                     | VAS_045_Credit
 * 26  | Cost Center                                | VAS_045_CostCenter
 * 27  | Business Partner                           | VAS_045_BusinessPartner
 * 28  | Product                                    | VAS_045_Product
 * 29  | Project                                    | VAS_045_Project
 * 30  | Total                                      | VAS_045_Total
 * 31  | Approving                                  | VAS_045_ApprovingJournal
 * 32  | Posting                                    | VAS_045_PostingJournal
 * 33  | Journal process failed                     | VAS_045_JournalProcessFailed
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_045_GLJournalPendingWidget = function () {

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $root = $('<div class="VAS-gljpq-root">');

        var $detailDialog = null;
        var $detailBody = null;
        var $detailBusy = null;

        /*
         * Process buttons already exist in the design.
         * These variables bind the buttons to the controller.
         */
        var $approveButton = null;
        var $postButton = null;

        var currentData = null;
        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;
        var refreshTimer = null;

        var selectedJournalId = 0;
        var selectedJournalStatus = "";
        var journalActionInProgress = false;

        var queueRequest = null;
        var detailRequest = null;
        var actionRequest = null;

        var isDisposed = false;

        var baseUrl =
            VIS.Application.contextUrl;

        var PILL_CLASS = {
            "DR": "VAS-gljpq-pill-draft",
            "CO": "VAS-gljpq-pill-posted",
            "CL": "VAS-gljpq-pill-posted",
            "IP": "VAS-gljpq-pill-submit",
            "AP": "VAS-gljpq-pill-approved",
            "NA": "VAS-gljpq-pill-pending",
            "RE": "VAS-gljpq-pill-returned"
        };

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            loadData();

            refreshTimer = setInterval(
                function () {
                    if (
                        !isDisposed &&
                        !journalActionInProgress
                    ) {
                        $self.refreshWidget();
                    }
                },
                1000 * 60 * 5
            );
        };

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);

            return text &&
                text !== "[" + key + "]"
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

        function fmtAmt(
            amount,
            precision
        ) {
            var stdPrecision = 2;

            if (
                VIS &&
                VIS.Env &&
                VIS.Env.getCtx &&
                VIS.Env
                    .getCtx()
                    .getStdPrecision
            ) {
                stdPrecision = Number(
                    VIS.Env
                        .getCtx()
                        .getStdPrecision()
                );
            }

            if (
                typeof precision === "number" &&
                precision >= 0
            ) {
                stdPrecision = precision;
            }

            if (
                isNaN(stdPrecision) ||
                stdPrecision < 0
            ) {
                stdPrecision = 2;
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
                        stdPrecision,

                    maximumFractionDigits:
                        stdPrecision
                }
            );
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

        function getAjaxErrorMessage(
            xhr,
            fallback
        ) {
            if (!xhr) {
                return fallback;
            }

            var data = normalizeResponse(
                xhr.responseText
            );

            if (!data) {
                return fallback;
            }

            return (
                data.errorText ||
                data.error ||
                data.message ||
                fallback
            );
        }

        function getJournalId(item) {
            if (!item) {
                return 0;
            }

            var id =
                item.GL_Journal_ID;

            if (
                id === undefined ||
                id === null ||
                id === ""
            ) {
                id = item.JournalID;
            }

            if (
                id === undefined ||
                id === null ||
                id === ""
            ) {
                id = item.journalId;
            }

            if (
                id === undefined ||
                id === null ||
                id === ""
            ) {
                id = item.GLJournalID;
            }

            if (
                id === undefined ||
                id === null ||
                id === ""
            ) {
                id = item.GL_JournalId;
            }

            id = parseInt(id, 10);

            return isNaN(id)
                ? 0
                : id;
        }

        function createBusyIndicator() {
            var $bsy = $(
                '<div id="VAS-gljpq-busy-' +
                $self.AD_UserHomeWidgetID +
                '" class="vis-busyindicatorouterwrap">' +

                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                "</div>" +

                "</div>"
            );

            $root.append($bsy);

            showBusy(false);
        }

        function showBusy(show) {
            if (!$root) {
                return;
            }

            var $busy = $root.find(
                "#VAS-gljpq-busy-" +
                $self.AD_UserHomeWidgetID
            );

            if (show) {
                $busy.show();
            }
            else {
                $busy.hide();
            }
        }

        function createWidget() {
            var id =
                $self.AD_UserHomeWidgetID;

            var clockIcon =
                '<svg viewBox="0 0 24 24" ' +
                'fill="none" ' +
                'stroke="currentColor" ' +
                'stroke-width="2" ' +
                'stroke-linecap="round" ' +
                'stroke-linejoin="round">' +

                '<circle cx="12" cy="12" r="10"></circle>' +
                '<polyline points="12 6 12 12 16 14"></polyline>' +

                "</svg>";

            var html =
                '<div class="VAS-gljpq-card">' +

                '<div class="w-head">' +

                '<div class="VAS-gljpq-icon">' +
                clockIcon +
                "</div>" +

                '<div class="w-title">' +
                esc(
                    lbl(
                        "VAS_045_PendingActionQueue",
                        "Pending Action Queue"
                    )
                ) +
                "</div>" +

                '<span class="VAS-gljpq-count" ' +
                'id="VAS-gljpq-count-' +
                id +
                '"></span>' +

                "</div>" +

                '<div class="VAS-gljpq-body" ' +
                'id="VAS-gljpq-body-' +
                id +
                '"></div>' +

                '<div class="VAS-gljpq-pager">' +

                '<button type="button" ' +
                'class="VAS-gljpq-page-btn VAS-gljpq-prev" ' +
                'aria-label="' +
                esc(
                    lbl(
                        "VIS_Previous",
                        "Previous"
                    )
                ) +
                '">&#8249;</button>' +

                '<span class="VAS-gljpq-page-text"></span>' +

                '<button type="button" ' +
                'class="VAS-gljpq-page-btn VAS-gljpq-next" ' +
                'aria-label="' +
                esc(
                    lbl(
                        "VIS_Next",
                        "Next"
                    )
                ) +
                '">&#8250;</button>' +

                "</div>" +

                "</div>";

            $root.append(html);

            $root.on(
                "click",
                ".VAS-gljpq-prev",
                function () {
                    if (pageNo <= 1) {
                        return;
                    }

                    pageNo--;

                    render(
                        currentData || {}
                    );
                }
            );

            $root.on(
                "click",
                ".VAS-gljpq-next",
                function () {
                    if (
                        totalPages <= 1 ||
                        pageNo >= totalPages
                    ) {
                        return;
                    }

                    pageNo++;

                    render(
                        currentData || {}
                    );
                }
            );

            createDetailDialog(
                clockIcon
            );
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            if (
                queueRequest &&
                queueRequest.readyState !== 4
            ) {
                queueRequest.abort();
            }

            showBusy(true);

            queueRequest = $.ajax({
                url:
                    baseUrl +
                    "VAS/VAS_045_GLJournalPendingWidget/GetPendingQueue",

                type: "GET",
                dataType: "json",
                cache: false,

                success: function (result) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        normalizeResponse(result);

                    if (!data) {
                        showError();
                        return;
                    }

                    if (
                        data.success === false ||
                        data.error
                    ) {
                        showError(
                            data.errorText ||
                            data.error
                        );

                        return;
                    }

                    if (!data.Queue) {
                        showError();
                        return;
                    }

                    currentData = data;
                    pageNo = 1;

                    render(data);
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

                    showError(
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                "VIS_Error",
                                "Error loading data."
                            )
                        )
                    );
                },

                complete: function () {
                    queueRequest = null;

                    if (!isDisposed) {
                        showBusy(false);
                    }
                }
            });
        }

        function render(data) {
            var id =
                $self.AD_UserHomeWidgetID;

            var $body = $root.find(
                "#VAS-gljpq-body-" +
                id
            );

            var queue =
                data.Queue || [];

            var precision =
                data.StdPrecision;

            $root.find(
                "#VAS-gljpq-count-" +
                id
            ).text(
                Number(
                    data.TotalCount ||
                    queue.length ||
                    0
                ).toLocaleString(
                    window.navigator.language
                ) +
                " " +
                lbl(
                    "VAS_045_Items",
                    "items"
                )
            );

            if (queue.length === 0) {
                $body.addClass(
                    "is-empty"
                );

                $body.html(
                    '<div class="VAS-gljpq-empty">' +
                    esc(
                        lbl(
                            "VIS_NoData",
                            "No pending journals."
                        )
                    ) +
                    "</div>"
                );

                totalPages = 0;

                updatePager();
                return;
            }

            $body.removeClass(
                "is-empty"
            );

            totalPages = Math.ceil(
                queue.length /
                pageSize
            );

            if (pageNo > totalPages) {
                pageNo = totalPages;
            }

            if (pageNo < 1) {
                pageNo = 1;
            }

            var start =
                (pageNo - 1) *
                pageSize;

            var pageQueue =
                queue.slice(
                    start,
                    start + pageSize
                );

            var html =
                '<div class="VAS-gljpq-list">';

            for (
                var index = 0;
                index < pageQueue.length;
                index++
            ) {
                var item =
                    pageQueue[index];

                var journalId =
                    getJournalId(item);

                var titleText =
                    esc(
                        item.DocumentNo
                    );

                if (item.Description) {
                    titleText +=
                        " · " +
                        esc(item.Description);
                }

                var ageLabel =
                    item.IsOverdue
                        ? (
                            esc(item.AgeStr) +
                            " " +
                            esc(
                                lbl(
                                    "VAS_045_Overdue",
                                    "overdue"
                                )
                            )
                        )
                        : esc(item.AgeStr);

                var itemStatus =
                    String(
                        item.DocStatus ||
                        ""
                    ).toUpperCase();

                var actionLabelHtml =
                    itemStatus === "AP"
                        ? (
                            '<span class="VAS-gljpq-meta-approved">' +
                            esc(item.ActionLabel) +
                            "</span>"
                        )
                        : esc(item.ActionLabel);

                var metaParts = [
                    actionLabelHtml,
                    ageLabel
                ];

                if (item.UserName) {
                    metaParts.push(
                        esc(item.UserName)
                    );
                }

                var amountText =
                    fmtAmt(
                        item.TotalDebit,
                        precision
                    );

                var markerType =
                    esc(
                        item.MarkerType ||
                        "normal"
                    );

                var itemInfo = esc(
                    String(
                        item.DocumentNo || ""
                    ) +
                    " - " +
                    String(
                        item.ActionLabel || ""
                    ) +
                    ", " +
                    String(
                        item.AgeStr || ""
                    ) +
                    ", " +
                    amountText
                );

                html +=
                    '<div class="VAS-gljpq-item" ' +
                    'data-journal-id="' +
                    journalId +
                    '" ' +
                    'title="' +
                    itemInfo +
                    '">' +

                    '<div class="VAS-gljpq-mrk ' +
                    "VAS-gljpq-mrk-" +
                    markerType +
                    '"></div>' +

                    '<div class="VAS-gljpq-body-row">' +

                    '<div class="VAS-gljpq-title">' +
                    titleText +
                    "</div>" +

                    '<div class="VAS-gljpq-meta">' +
                    metaParts.join(" · ") +
                    "</div>" +

                    "</div>" +

                    '<span class="VAS-gljpq-amt">' +
                    esc(amountText) +
                    "</span>" +

                    "</div>";
            }

            html += "</div>";

            $body.html(html);

            $body
                .find(".VAS-gljpq-item")
                .off("click")
                .on(
                    "click",
                    function () {
                        var journalId =
                            parseInt(
                                $(this).attr(
                                    "data-journal-id"
                                ),
                                10
                            );

                        openDetailDialog(
                            journalId
                        );
                    }
                );

            updatePager();
        }

        function updatePager() {
            var $pageText =
                $root.find(
                    ".VAS-gljpq-page-text"
                );

            var $prevButton =
                $root.find(
                    ".VAS-gljpq-prev"
                );

            var $nextButton =
                $root.find(
                    ".VAS-gljpq-next"
                );

            if (totalPages > 1) {
                $pageText.text(
                    pageNo +
                    " " +
                    lbl(
                        "VIS_Of",
                        "of"
                    ) +
                    " " +
                    totalPages
                );
            }
            else {
                $pageText.text("");
            }

            $prevButton.prop(
                "disabled",
                pageNo <= 1 ||
                totalPages <= 1
            );

            $nextButton.prop(
                "disabled",
                totalPages <= 1 ||
                pageNo >= totalPages
            );
        }

        function createDetailDialog(
            svgIcon
        ) {
            var id =
                $self.AD_UserHomeWidgetID;

            $detailDialog = $(
                '<div class="VAS-gljpq-dialog" ' +
                'id="VAS-gljpq-dialog-' +
                id +
                '" ' +
                'role="dialog" ' +
                'aria-modal="true">' +

                '<div class="VAS-gljpq-dialog-scrim"></div>' +

                '<div class="VAS-gljpq-dialog-card">' +

                '<div class="VAS-gljpq-dialog-head">' +

                '<div class="VAS-gljpq-dialog-icon">' +
                svgIcon +
                "</div>" +

                '<div class="VAS-gljpq-dialog-title-wrap">' +

                '<div class="VAS-gljpq-dialog-title" ' +
                'id="VAS-gljpq-dialog-title-' +
                id +
                '">&mdash;</div>' +

                '<div class="VAS-gljpq-dialog-sub" ' +
                'id="VAS-gljpq-dialog-sub-' +
                id +
                '">&mdash;</div>' +

                "</div>" +

                '<button type="button" ' +
                'class="VAS-gljpq-dialog-close-x" ' +
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
                'stroke-width="1.8" ' +
                'stroke-linecap="round" ' +
                'stroke-linejoin="round">' +

                '<line x1="18" y1="6" x2="6" y2="18"></line>' +
                '<line x1="6" y1="6" x2="18" y2="18"></line>' +

                "</svg>" +

                "</button>" +

                "</div>" +

                '<div class="VAS-gljpq-dialog-body">' +

                '<div class="VAS-gljpq-dialog-busy">' +

                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                "</div>" +

                "</div>" +

                '<div class="VAS-gljpq-detail-content" ' +
                'id="VAS-gljpq-detail-body-' +
                id +
                '"></div>' +

                "</div>" +

                '<div class="VAS-gljpq-dialog-footer">' +

                '<button type="button" ' +
                'class="VAS-gljpq-dialog-secondary ' +
                'VAS-gljpq-detail-close">' +

                esc(
                    lbl(
                        "VAS_Close",
                        "Close"
                    )
                ) +

                "</button>" +

                '<div class="VAS-gljpq-dialog-actions">' +

                '<button type="button" ' +
                'class="VAS-gljpq-dialog-secondary ' +
                'VAS-gljpq-action-approve">' +

                esc(
                    lbl(
                        "VAS_041_Approve",
                        "Approve"
                    )
                ) +

                "</button>" +

                '<button type="button" ' +
                'class="VAS-gljpq-dialog-primary ' +
                'VAS-gljpq-action-post">' +

                esc(
                    lbl(
                        "VAS_041_PostJournal",
                        "Post journal"
                    )
                ) +

                "</button>" +

                "</div>" +

                "</div>" +

                "</div>" +

                "</div>"
            );

            $detailDialog.hide();

            $detailBody =
                $detailDialog.find(
                    "#VAS-gljpq-detail-body-" +
                    id
                );

            $detailBusy =
                $detailDialog.find(
                    ".VAS-gljpq-dialog-busy"
                );

            /*
             * Bind the existing design buttons.
             */
            $approveButton =
                $detailDialog.find(
                    ".VAS-gljpq-action-approve"
                );

            $postButton =
                $detailDialog.find(
                    ".VAS-gljpq-action-post"
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

            $detailDialog
                .find(
                    ".VAS-gljpq-dialog-close-x, " +
                    ".VAS-gljpq-detail-close, " +
                    ".VAS-gljpq-dialog-scrim"
                )
                .on(
                    "click",
                    closeDetailDialog
                );

            $(document).on(
                "keydown.VAS-gljpq-" +
                id,

                function (event) {
                    if (
                        event.key === "Escape" &&
                        $detailDialog &&
                        $detailDialog.is(":visible") &&
                        !journalActionInProgress
                    ) {
                        closeDetailDialog();
                    }
                }
            );

            $("body").append(
                $detailDialog
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

            if (!$detailDialog) {
                return;
            }

            resetDetailDialog();

            selectedJournalId =
                isNaN(journalId)
                    ? 0
                    : journalId;

            selectedJournalStatus = "";

            updateActionButtons();

            $detailDialog.show();

            $("body").addClass(
                "VAS-gljpq-body-lock"
            );

            if (selectedJournalId <= 0) {
                renderDetailError(
                    "Invalid journal id."
                );

                return;
            }

            loadJournalDetail(
                selectedJournalId
            );
        }

        function resetDetailDialog() {
            var id =
                $self.AD_UserHomeWidgetID;

            $detailDialog.find(
                "#VAS-gljpq-dialog-title-" +
                id
            ).text("—");

            $detailDialog.find(
                "#VAS-gljpq-dialog-sub-" +
                id
            ).text("—");

            if ($detailBody) {
                $detailBody.html("");
            }

            resetActionButtonLabels();
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

            updateActionButtons();

            $detailDialog.hide();

            $("body").removeClass(
                "VAS-gljpq-body-lock"
            );
        }

        function showDetailBusy(show) {
            if (!$detailBusy) {
                return;
            }

            if (show) {
                $detailBusy.show();
            }
            else {
                $detailBusy.hide();
            }
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
                isNaN(journalId) ||
                journalId <= 0
            ) {
                renderDetailError(
                    "Invalid journal id."
                );

                return;
            }

            if (
                detailRequest &&
                detailRequest.readyState !== 4
            ) {
                detailRequest.abort();
            }

            showDetailBusy(true);

            if ($detailBody) {
                $detailBody.html("");
            }

            detailRequest = $.ajax({
                url:
                    baseUrl +
                    "VAS/VAS_041_GLJournalEntriesWidget/GetJournalEntryDetail",

                type: "GET",
                dataType: "json",
                cache: false,

                data: {
                    journalId: journalId
                },

                success: function (result) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        normalizeResponse(result);

                    if (!data) {
                        renderDetailError(
                            "No response from server."
                        );

                        return;
                    }

                    if (
                        data.error === true ||
                        data.error
                    ) {
                        renderDetailError(
                            data.errorText ||
                            data.error ||
                            (
                                "Journal details not found for ID: " +
                                journalId
                            )
                        );

                        return;
                    }

                    if (
                        !data.Journal &&
                        data.journal
                    ) {
                        data.Journal =
                            data.journal;
                    }

                    if (
                        !data.Lines &&
                        data.lines
                    ) {
                        data.Lines =
                            data.lines;
                    }

                    if (
                        !data.Lines &&
                        data.JournalLines
                    ) {
                        data.Lines =
                            data.JournalLines;
                    }

                    if (
                        !data.Lines &&
                        data.journalLines
                    ) {
                        data.Lines =
                            data.journalLines;
                    }

                    if (!data.Journal) {
                        renderDetailError(
                            lbl(
                                "VAS_045_JournalObjectNotReturned",
                                "Journal object was not returned from server."
                            )
                        );

                        return;
                    }

                    renderJournalDetail(data);
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

                    renderDetailError(
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                "VIS_Error",
                                "Error loading journal details."
                            )
                        )
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
                data.Journal ||
                data.journal ||
                {};

            var lines =
                data.Lines ||
                data.lines ||
                data.JournalLines ||
                data.journalLines ||
                [];

            var precision =
                data.StdPrecision;

            if (
                precision === undefined ||
                precision === null
            ) {
                precision =
                    data.stdPrecision;
            }

            /*
             * Store the current document status.
             * Approve is enabled for DR, IP and NA.
             * Post is enabled for AP.
             */
            selectedJournalStatus =
                String(
                    journal.DocStatus ||
                    journal.docStatus ||
                    ""
                ).toUpperCase();

            var returnedJournalId =
                parseInt(
                    journal.GL_Journal_ID ||
                    journal.GLJournalID ||
                    journal.journalId ||
                    selectedJournalId,
                    10
                );

            if (
                !isNaN(returnedJournalId) &&
                returnedJournalId > 0
            ) {
                selectedJournalId =
                    returnedJournalId;
            }

            updateActionButtons();

            var pillClass =
                PILL_CLASS[
                    selectedJournalStatus
                ] ||
                "VAS-gljpq-pill-draft";

            var id =
                $self.AD_UserHomeWidgetID;

            var totalDebit =
                fmtAmt(
                    journal.TotalDebit,
                    precision
                );

            var totalCredit =
                fmtAmt(
                    journal.TotalCredit,
                    precision
                );

            var book =
                journal.AccountingBook ||
                "Primary";

            if (data.ISOCode) {
                book +=
                    " · " +
                    data.ISOCode;
            }

            $detailDialog.find(
                "#VAS-gljpq-dialog-title-" +
                id
            ).text(
                (journal.DocumentNo || "") +
                (
                    journal.Description
                        ? (
                            " · " +
                            journal.Description
                        )
                        : ""
                )
            );

            $detailDialog.find(
                "#VAS-gljpq-dialog-sub-" +
                id
            ).text(
                (
                    journal.StatusName ||
                    selectedJournalStatus ||
                    ""
                ) +
                (
                    journal.DateAcct
                        ? (
                            " · " +
                            journal.DateAcct
                        )
                        : ""
                )
            );

            var html =
                '<div class="VAS-gljpq-detail-summary">' +

                "<div>" +
                "<span>" +
                esc(
                    lbl(
                        "VAS_045_JournalNo",
                        "Journal No."
                    )
                ) +
                "</span>" +
                "<strong>" +
                esc(journal.DocumentNo) +
                "</strong>" +
                "</div>" +

                "<div>" +
                "<span>" +
                esc(
                    lbl(
                        "VAS_045_Date",
                        "Date"
                    )
                ) +
                "</span>" +
                "<strong>" +
                esc(journal.DateAcct) +
                "</strong>" +
                "</div>" +

                "<div>" +
                "<span>" +
                esc(
                    lbl(
                        "VAS_045_Status",
                        "Status"
                    )
                ) +
                "</span>" +

                "<strong>" +

                '<span class="VAS-gljpq-pill ' +
                pillClass +
                '">' +

                "<span></span>" +

                esc(
                    journal.StatusName ||
                    selectedJournalStatus
                ) +

                "</span>" +

                "</strong>" +
                "</div>" +

                "<div>" +
                "<span>" +
                esc(
                    lbl(
                        "VAS_045_AccountingBook",
                        "Accounting Book"
                    )
                ) +
                "</span>" +
                "<strong>" +
                esc(book) +
                "</strong>" +
                "</div>" +

                "<div>" +
                "<span>" +
                esc(
                    lbl(
                        "VAS_045_TotalDebit",
                        "Total Debit"
                    )
                ) +
                "</span>" +
                "<strong>" +
                esc(totalDebit) +
                "</strong>" +
                "</div>" +

                "<div>" +
                "<span>" +
                esc(
                    lbl(
                        "VAS_045_TotalCredit",
                        "Total Credit"
                    )
                ) +
                "</span>" +
                "<strong>" +
                esc(totalCredit) +
                "</strong>" +
                "</div>" +

                '<div class="VAS-gljpq-detail-description">' +
                "<span>" +
                esc(
                    lbl(
                        "VAS_045_Description",
                        "Description"
                    )
                ) +
                "</span>" +
                "<strong>" +
                esc(journal.Description) +
                "</strong>" +
                "</div>" +

                "</div>" +

                '<div class="VAS-gljpq-detail-section-title">' +
                esc(
                    lbl(
                        "VAS_045_JournalLines",
                        "Journal Lines"
                    )
                ) +
                "</div>" +

                '<div class="VAS-gljpq-detail-lines-wrap">' +

                '<table class="VAS-gljpq-detail-lines">' +

                "<thead>" +
                "<tr>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_Account",
                        "Account"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_Debit",
                        "Debit"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_Credit",
                        "Credit"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_CostCenter",
                        "Cost Center"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_BusinessPartner",
                        "Business Partner"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_Product",
                        "Product"
                    )
                ) +
                "</th>" +

                "<th>" +
                esc(
                    lbl(
                        "VAS_045_Project",
                        "Project"
                    )
                ) +
                "</th>" +

                "</tr>" +
                "</thead>" +

                "<tbody>";

            if (
                !lines ||
                lines.length === 0
            ) {
                html +=
                    "<tr>" +

                    '<td colspan="7" ' +
                    'class="VAS-gljpq-detail-empty-line">' +

                    esc(
                        lbl(
                            "VAS_045_NoJournalLinesReturned",
                            "No journal lines were returned from server."
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

                    var accountText = "";

                    if (
                        line.AccountCode &&
                        line.AccountName
                    ) {
                        accountText =
                            line.AccountCode +
                            " · " +
                            line.AccountName;
                    }
                    else if (line.AccountCode) {
                        accountText =
                            line.AccountCode;
                    }
                    else if (line.AccountName) {
                        accountText =
                            line.AccountName;
                    }
                    else {
                        accountText = "-";
                    }

                    html +=
                        "<tr>" +

                        "<td>" +
                        esc(accountText) +
                        "</td>" +

                        '<td class="VAS-gljpq-detail-amt">' +
                        esc(
                            Number(
                                line.Debit || 0
                            ) > 0
                                ? fmtAmt(
                                    line.Debit,
                                    precision
                                )
                                : "-"
                        ) +
                        "</td>" +

                        '<td class="VAS-gljpq-detail-amt">' +
                        esc(
                            Number(
                                line.Credit || 0
                            ) > 0
                                ? fmtAmt(
                                    line.Credit,
                                    precision
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

                "<td>" +
                esc(
                    lbl(
                        "VAS_045_Total",
                        "Total"
                    )
                ) +
                "</td>" +

                '<td class="VAS-gljpq-detail-amt">' +
                esc(totalDebit) +
                "</td>" +

                '<td class="VAS-gljpq-detail-amt">' +
                esc(totalCredit) +
                "</td>" +

                '<td colspan="4"></td>' +

                "</tr>" +
                "</tfoot>" +

                "</table>" +
                "</div>" +

                '<div class="VAS-gljpq-created-strip">' +

                '<span class="VAS-gljpq-avatar">' +
                esc(
                    initials(
                        journal.CreatedByName
                    )
                ) +
                "</span>" +

                "<div>" +

                "<span>" +
                esc(
                    lbl(
                        "VAS_045_CreatedBy",
                        "Created By"
                    )
                ) +
                "</span>" +

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

            if ($detailBody) {
                $detailBody.html(html);
            }
        }

        /*
         * Approve is available for:
         * DR = Draft
         * IP = In Progress
         * NA = Not Approved
         *
         * Post journal is available for:
         * AP = Approved
         */
        function updateActionButtons() {
            var status =
                String(
                    selectedJournalStatus ||
                    ""
                ).toUpperCase();

            var canApprove =
                status === "DR" ||
                status === "IP" ||
                status === "NA";

            var canPost =
                status === "AP";

            if ($approveButton) {
                $approveButton.toggle(
                    canApprove
                );

                $approveButton.prop(
                    "disabled",
                    journalActionInProgress ||
                    !canApprove ||
                    selectedJournalId <= 0
                );
            }

            if ($postButton) {
                $postButton.toggle(
                    canPost
                );

                $postButton.prop(
                    "disabled",
                    journalActionInProgress ||
                    !canPost ||
                    selectedJournalId <= 0
                );
            }
        }

        function executeJournalAction(
            actionName,
            actionType
        ) {
            if (
                isDisposed ||
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
                status !== "AP"
            ) {
                return;
            }

            journalActionInProgress = true;

            setActionButtonBusy(
                true,
                actionType
            );

            actionRequest = $.ajax({
                url:
                    baseUrl +
                    "VAS/VAS_045_GLJournalPendingWidget/" +
                    actionName,

                type: "POST",
                dataType: "json",
                cache: false,

                data: {
                    journalId:
                        selectedJournalId
                },

                success: function (result) {
                    if (isDisposed) {
                        return;
                    }

                    var data =
                        normalizeResponse(result);

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
                                "VAS_045_JournalProcessFailed",
                                "Journal process failed."
                            )
                        );

                        return;
                    }

                    selectedJournalStatus =
                        String(
                            data.docStatus ||
                            selectedJournalStatus ||
                            ""
                        ).toUpperCase();

                    /*
                     * Refresh the pending queue after every successful process.
                     */
                    loadData();

                    if (actionType === "approve") {
                        /*
                         * Approve changes the status to AP.
                         * Reload details and enable Post journal.
                         */
                        journalActionInProgress = false;

                        setActionButtonBusy(
                            false,
                            actionType
                        );

                        loadJournalDetail(
                            selectedJournalId
                        );

                        return;
                    }

                    /*
                     * Posting completes the process.
                     * The record is removed from the pending queue.
                     */
                    journalActionInProgress = false;

                    setActionButtonBusy(
                        false,
                        actionType
                    );

                    closeDetailDialog();
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

                    showProcessError(
                        getAjaxErrorMessage(
                            xhr,
                            lbl(
                                "VAS_045_JournalProcessFailed",
                                "Journal process failed."
                            )
                        )
                    );
                },

                complete: function () {
                    actionRequest = null;

                    if (
                        isDisposed ||
                        !journalActionInProgress
                    ) {
                        return;
                    }

                    journalActionInProgress = false;

                    setActionButtonBusy(
                        false,
                        actionType
                    );
                }
            });
        }

        function setActionButtonBusy(
            busy,
            actionType
        ) {
            showDetailBusy(busy);

            if ($approveButton) {
                $approveButton.text(
                    busy &&
                    actionType === "approve"
                        ? lbl(
                            "VAS_045_ApprovingJournal",
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
                            "VAS_045_PostingJournal",
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

        function resetActionButtonLabels() {
            if ($approveButton) {
                $approveButton.text(
                    lbl(
                        "VAS_041_Approve",
                        "Approve"
                    )
                );
            }

            if ($postButton) {
                $postButton.text(
                    lbl(
                        "VAS_041_PostJournal",
                        "Post journal"
                    )
                );
            }
        }

        function showProcessError(message) {
            /*
             * Keep the detail popup open and show the real
             * validation/process message returned by the controller.
             */
            window.alert(
                message ||
                lbl(
                    "VAS_045_JournalProcessFailed",
                    "Journal process failed."
                )
            );
        }

        function renderDetailError(message) {
            var text =
                lbl(
                    "VIS_Error",
                    "Error loading data."
                );

            if (
                typeof message === "string" &&
                message.length > 0
            ) {
                text = message;
            }

            if ($detailBody) {
                $detailBody.html(
                    '<div class="VAS-gljpq-dialog-empty">' +
                    esc(text) +
                    "</div>"
                );
            }

            selectedJournalStatus = "";

            updateActionButtons();
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

        function showError(message) {
            var id =
                $self.AD_UserHomeWidgetID;

            var text =
                lbl(
                    "VIS_Error",
                    "Error loading data."
                );

            if (
                typeof message === "string" &&
                message.length > 0
            ) {
                text = message;
            }

            $root.find(
                "#VAS-gljpq-body-" +
                id
            )
                .addClass(
                    "is-empty"
                )
                .html(
                    '<div class="VAS-gljpq-empty">' +
                    esc(text) +
                    "</div>"
                );

            totalPages = 0;

            updatePager();
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;

            if (refreshTimer) {
                clearInterval(
                    refreshTimer
                );

                refreshTimer = null;
            }

            if (
                queueRequest &&
                queueRequest.readyState !== 4
            ) {
                queueRequest.abort();
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

            queueRequest = null;
            detailRequest = null;
            actionRequest = null;

            if (
                $self &&
                $self.AD_UserHomeWidgetID
            ) {
                $(document).off(
                    "keydown.VAS-gljpq-" +
                    $self.AD_UserHomeWidgetID
                );
            }

            $("body").removeClass(
                "VAS-gljpq-body-lock"
            );

            if ($detailDialog) {
                $detailDialog.remove();
                $detailDialog = null;
            }

            if ($root) {
                $root.off();
                $root.remove();
            }

            $detailBody = null;
            $detailBusy = null;

            $approveButton = null;
            $postButton = null;

            selectedJournalId = 0;
            selectedJournalStatus = "";

            currentData = null;

            $root = null;
            $self = null;
        };
    };

    VAS.VAS_045_GLJournalPendingWidget.prototype.init =
        function (
            windowNo,
            frame
        ) {
            this.frame = frame;
            this.windowNo = windowNo;

            if (
                frame &&
                frame.widgetInfo
            ) {
                this.AD_UserHomeWidgetID =
                    frame
                        .widgetInfo
                        .AD_UserHomeWidgetID;
            }

            this.Initalize();

            if (
                this.frame &&
                this.frame.getContentGrid
            ) {
                this.frame
                    .getContentGrid()
                    .append(
                        this.getRoot()
                    );
            }
        };

    VAS.VAS_045_GLJournalPendingWidget.prototype.widgetSizeChange =
        function (
            height,
            width
        ) {
        };

    VAS.VAS_045_GLJournalPendingWidget.prototype.dispose =
        function () {
            this.disposeComponent();

            if (
                this.frame &&
                this.frame.dispose
            ) {
                this.frame.dispose();
            }

            this.frame = null;
        };

})(VAS, jQuery);
