/**
 * GL Journal Recent Entries Widget
 * Purpose  : Display the 6 most recent GL_Journal documents with document
 *            number, account date, description, status and total debit / credit.
 *            Clicking a row zooms to that document in the GL Journal window.
 * Tables   : GL_Journal, GL_JournalLine, AD_Ref_List, C_AcctSchema, C_Currency
 */


/*
 * ============================================================
 * VAS_044_GLJournalRecentWidget Messages
 * ============================================================
 *
 * Custom Messages:
 *
 * Value                                English Text                          Arabic Text
 * ---------------------------------------------------------------------------------------------------------
 * VAS_044_RecentJournalEntries         Recent Journal Entries               قيود اليومية الحديثة
 * VAS_044_Hash                         #                                    #
 * VAS_044_Date                         Date                                 التاريخ
 * VAS_044_Description                  Description                          الوصف
 * VAS_044_Status                       Status                               الحالة
 * VAS_044_Debit                        Debit                                مدين
 * VAS_044_Credit                       Credit                               دائن
 * VAS_044_JournalNo                    Journal No.                          رقم القيد
 * VAS_044_AccountingBook               Accounting Book                      الدفتر المحاسبي
 * VAS_044_TotalDebit                   Total Debit                          إجمالي المدين
 * VAS_044_TotalCredit                  Total Credit                         إجمالي الدائن
 * VAS_044_JournalLines                 Journal Lines                        سطور القيد
 * VAS_044_Account                      Account                              الحساب
 * VAS_044_CostCenter                   Cost Center                          مركز التكلفة
 * VAS_044_BusinessPartner              Business Partner                     شريك العمل
 * VAS_044_Product                      Product                              المنتج
 * VAS_044_Project                      Project                              المشروع
 * VAS_044_Total                        Total                                الإجمالي
 * VAS_044_CreatedBy                    Created By                           تم الإنشاء بواسطة
 * VAS_044_Drafted                      Drafted                              تم إنشاء المسودة
 * VAS_044_NoJournalLines               No journal lines.                    لا توجد سطور للقيد
 * VAS_044_Approving                    Approving...                         جارٍ تنفيذ الموافقة...
 * VAS_044_Posting                      Posting...                           جارٍ ترحيل القيد...
 * VAS_044_JournalProcessFailed         Journal process failed.              فشلت معالجة القيد
 * VAS_044_DetailsNotLoaded             Journal details are not loaded.      لم يتم تحميل تفاصيل القيد
 * VAS_044_DetailsNotAvailable          Journal details are not available.   تفاصيل القيد غير متوفرة
 * VAS_044_PrintWindowFailed            Could not open the print window.     تعذر فتح نافذة الطباعة
 *
 * Shared Messages:
 *
 * Value                                English Text                          Arabic Text
 * ---------------------------------------------------------------------------------------------------------
 * VAS_DownloadPDF                      Download PDF                         تنزيل PDF
 * VAS_041_Approve                      Approve                              موافقة
 * VAS_041_PostJournal                  Post journal                         ترحيل القيد
 * VAS_Close                            Close                                إغلاق
 *
 * Existing VIS Messages:
 *
 * Value                                English Text
 * ---------------------------------------------------------------------------------------------------------
 * VIS_Previous                         Previous
 * VIS_Next                             Next
 * VIS_Of                               of
 * VIS_NoData                           No data available.
 * VIS_Error                            Error loading data.
 *
 * ============================================================
 */



/**
 * GL Journal Recent Entries Widget
 * Displays recent GL Journals and supports:
 * 1. Approve Journal
 * 2. Post Journal
 * 3. Print / Save popup details as PDF
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    function lbl(key, fallback) {
        var text = VIS.Msg.getMsg(key);

        return text &&
            text.charAt(0) !== "["
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
        var standardPrecision = 2;

        try {
            if (
                VIS &&
                VIS.Env &&
                VIS.Env.getCtx &&
                VIS.Env
                    .getCtx()
                    .getStdPrecision
            ) {
                standardPrecision = Number(
                    VIS.Env
                        .getCtx()
                        .getStdPrecision()
                );
            }
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

    var PILL_CLASS = {
        "DR": "VAS-gljr-pill-draft",
        "CO": "VAS-gljr-pill-posted",
        "IP": "VAS-gljr-pill-submit",
        "AP": "VAS-gljr-pill-posted",
        "NA": "VAS-gljr-pill-pending",
        "VO": "VAS-gljr-pill-voided",
        "RE": "VAS-gljr-pill-reverse",
        "CL": "VAS-gljr-pill-closed"
    };

    VAS.VAS_044_GLJournalRecentWidget =
        function () {
            this.frame = null;
            this.windowNo = 0;
            this.AD_UserHomeWidgetID = 0;

            var $self = this;

            var $root = $(
                '<div class="VAS-gljr-root">'
            );

            var $detailDialog = null;
            var $detailBody = null;
            var $detailBusy = null;

            var $approveButton = null;
            var $postButton = null;
            var $downloadButton = null;

            var currentData = null;

            var pageNo = 1;
            var pageSize = 3;
            var totalPages = 0;

            var selectedJournalId = 0;
            var selectedJournalStatus = "";
            var selectedJournalPosted = false;

            var detailLoaded = false;

            var journalActionInProgress =
                false;

            var refreshTimer = null;
            var loadRequest = null;
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
                                loadData();
                            }
                        },
                        1000 * 60 * 5
                    );
            };

            function createBusyIndicator() {
                var $busy = $(
                    '<div id="VAS-gljr-busy-' +
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
                if (!$root) {
                    return;
                }

                var $busy = $root.find(
                    "#VAS-gljr-busy-" +
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
                var documentIcon =
                    '<svg viewBox="0 0 24 24" ' +
                    'fill="none" ' +
                    'stroke="currentColor" ' +
                    'stroke-width="2" ' +
                    'stroke-linecap="round" ' +
                    'stroke-linejoin="round">' +

                    '<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"></path>' +
                    '<polyline points="14 2 14 8 20 8"></polyline>' +

                    "</svg>";

                var id =
                    $self.AD_UserHomeWidgetID;

                var html =
                    '<div class="VAS-gljr-card">' +

                    '<div class="w-head">' +

                    '<div class="VAS-gljr-icon">' +
                    documentIcon +
                    "</div>" +

                    '<div class="w-title">' +
                    esc(
                        lbl(
                            "VAS_044_RecentJournalEntries",
                            "Recent Journal Entries"
                        )
                    ) +
                    "</div>" +

                    "</div>" +

                    '<div class="VAS-gljr-table-wrap">' +

                    '<table class="VAS-gljr-table">' +

                    "<thead>" +
                    "<tr>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Hash",
                            "#"
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

                    '<th class="VAS-gljr-num">' +
                    esc(
                        lbl(
                            "VAS_044_Debit",
                            "Debit"
                        )
                    ) +
                    "</th>" +

                    '<th class="VAS-gljr-num">' +
                    esc(
                        lbl(
                            "VAS_044_Credit",
                            "Credit"
                        )
                    ) +
                    "</th>" +

                    "</tr>" +
                    "</thead>" +

                    '<tbody id="VAS-gljr-tbody-' +
                    id +
                    '">' +

                    '<tr><td colspan="6" class="VAS-gljr-empty">—</td></tr>' +

                    "</tbody>" +

                    "</table>" +

                    "</div>" +

                    '<div class="VAS-gljr-pager">' +

                    '<button type="button" ' +
                    'class="VAS-gljr-page-btn VAS-gljr-prev" ' +
                    'aria-label="' +
                    esc(
                        lbl(
                            "VIS_Previous",
                            "Previous"
                        )
                    ) +
                    '">&#8249;</button>' +

                    '<span class="VAS-gljr-page-text"></span>' +

                    '<button type="button" ' +
                    'class="VAS-gljr-page-btn VAS-gljr-next" ' +
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
                    ".VAS-gljr-row",
                    function () {
                        openDetailDialog(
                            $(this).data("id")
                        );
                    }
                );

                $root.on(
                    "click",
                    ".VAS-gljr-prev",
                    function () {
                        if (pageNo <= 1) {
                            return;
                        }

                        pageNo--;

                        renderRows(
                            currentData || {},
                            id
                        );
                    }
                );

                $root.on(
                    "click",
                    ".VAS-gljr-next",
                    function () {
                        if (
                            totalPages <= 1 ||
                            pageNo >= totalPages
                        ) {
                            return;
                        }

                        pageNo++;

                        renderRows(
                            currentData || {},
                            id
                        );
                    }
                );

                createDetailDialog(
                    documentIcon
                );
            }

            function loadData() {
                if (isDisposed) {
                    return;
                }

                if (
                    loadRequest &&
                    loadRequest.readyState !== 4
                ) {
                    loadRequest.abort();
                }

                showBusy(true);

                loadRequest = $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_044_GLJournalRecentWidget/GetRecentEntries",

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
                            data.success === false ||
                            data.error
                        ) {
                            showEmpty(
                                data &&
                                (
                                    data.errorText ||
                                    data.error
                                )
                            );

                            return;
                        }

                        currentData = data;
                        pageNo = 1;

                        renderRows(
                            data,
                            $self
                                .AD_UserHomeWidgetID
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

                        showEmpty();
                    },

                    complete: function () {
                        loadRequest = null;

                        if (!isDisposed) {
                            showBusy(false);
                        }
                    }
                });
            }

            function renderRows(data, id) {
                var entries =
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

                var $tableBody =
                    $root.find(
                        "#VAS-gljr-tbody-" +
                        id
                    );

                if (entries.length === 0) {
                    $root.find(
                        ".VAS-gljr-table-wrap"
                    ).addClass(
                        "is-empty"
                    );

                    $tableBody.html(
                        '<tr class="VAS-gljr-empty-row">' +
                        '<td colspan="6" class="VAS-gljr-empty">' +
                        esc(
                            lbl(
                                "VIS_NoData",
                                "No data available."
                            )
                        ) +
                        "</td>" +
                        "</tr>"
                    );

                    totalPages = 0;
                    updatePager();
                    return;
                }

                $root.find(
                    ".VAS-gljr-table-wrap"
                ).removeClass(
                    "is-empty"
                );

                totalPages = Math.ceil(
                    entries.length /
                    pageSize
                );

                pageNo = Math.max(
                    1,
                    Math.min(
                        pageNo,
                        totalPages
                    )
                );

                var start =
                    (pageNo - 1) *
                    pageSize;

                var pageEntries =
                    entries.slice(
                        start,
                        start + pageSize
                    );

                var html = "";

                for (
                    var index = 0;
                    index < pageEntries.length;
                    index++
                ) {
                    var entry =
                        pageEntries[index];

                    var statusText =
                        entry.DocStatus ||
                        entry.StatusName ||
                        "";

                    var pillClass =
                        PILL_CLASS[
                            entry.DocStatus
                        ] ||
                        "VAS-gljr-pill-draft";

                    var debitText =
                        Number(
                            entry.TotalDebit || 0
                        ) > 0
                            ? (
                                symbol +
                                fmtAmt(
                                    entry.TotalDebit,
                                    precision
                                )
                            )
                            : "—";

                    var creditText =
                        Number(
                            entry.TotalCredit || 0
                        ) > 0
                            ? (
                                symbol +
                                fmtAmt(
                                    entry.TotalCredit,
                                    precision
                                )
                            )
                            : "—";

                    var rowClass =
                        "VAS-gljr-row" +
                        (
                            entry.IsUnbalanced
                                ? " VAS-gljr-row-unbal"
                                : ""
                        );

                    var rowInformation = esc(
                        entry.DocumentNo +
                        " - " +
                        statusText +
                        ", " +
                        lbl(
                            "VAS_044_Debit",
                            "Debit"
                        ) +
                        ": " +
                        debitText +
                        ", " +
                        lbl(
                            "VAS_044_Credit",
                            "Credit"
                        ) +
                        ": " +
                        creditText
                    );

                    html +=
                        '<tr class="' +
                        rowClass +
                        '" data-id="' +
                        entry.GL_Journal_ID +
                        '" title="' +
                        rowInformation +
                        '">' +

                        '<td class="VAS-gljr-col-id">' +
                        esc(
                            entry.DocumentNo
                        ) +
                        "</td>" +

                        '<td class="VAS-gljr-col-date">' +
                        esc(
                            entry.DateAcct
                        ) +
                        "</td>" +

                        '<td class="VAS-gljr-col-desc">' +
                        esc(
                            entry.Description
                        ) +
                        "</td>" +

                        "<td>" +

                        '<span class="VAS-gljr-pill ' +
                        pillClass +
                        '">' +

                        esc(
                            statusText
                        ) +

                        "</span>" +

                        "</td>" +

                        '<td class="VAS-gljr-col-num">' +
                        esc(debitText) +
                        "</td>" +

                        '<td class="VAS-gljr-col-num">' +
                        esc(creditText) +
                        "</td>" +

                        "</tr>";
                }

                $tableBody.html(html);
                updatePager();
            }

            function updatePager() {
                var $pageText =
                    $root.find(
                        ".VAS-gljr-page-text"
                    );

                var $previousButton =
                    $root.find(
                        ".VAS-gljr-prev"
                    );

                var $nextButton =
                    $root.find(
                        ".VAS-gljr-next"
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

                $previousButton.prop(
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
                    '<div class="VAS-gljr-detail-dialog" ' +
                    'id="VAS-gljr-detail-dialog-' +
                    id +
                    '" style="display:none" ' +
                    'role="dialog" ' +
                    'aria-modal="true">' +

                    '<div class="VAS-gljr-detail-scrim"></div>' +

                    '<div class="VAS-gljr-detail-card">' +

                    '<div class="VAS-gljr-detail-head">' +

                    '<div class="VAS-gljr-detail-icon">' +
                    svgIcon +
                    "</div>" +

                    '<div class="VAS-gljr-detail-title-wrap">' +

                    '<div class="VAS-gljr-detail-title" ' +
                    'id="VAS-gljr-detail-title-' +
                    id +
                    '">&mdash;</div>' +

                    '<div class="VAS-gljr-detail-sub" ' +
                    'id="VAS-gljr-detail-sub-' +
                    id +
                    '">&mdash;</div>' +

                    "</div>" +

                    '<button type="button" ' +
                    'class="VAS-gljr-detail-close-x" ' +
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

                    '<div class="VAS-gljr-detail-body">' +

                    '<div class="VAS-gljr-detail-busy">' +

                    '<div class="vis-busyindicatorinnerwrap">' +
                    '<i class="vis_widgetloader"></i>' +
                    "</div>" +

                    "</div>" +

                    '<div class="VAS-gljr-detail-content" ' +
                    'id="VAS-gljr-detail-body-' +
                    id +
                    '"></div>' +

                    "</div>" +

                    '<div class="VAS-gljr-detail-footer">' +

                    '<button type="button" ' +
                    'class="VAS-gljr-detail-secondary VAS-gljr-download">' +

                    esc(
                        lbl(
                            "VAS_DownloadPDF",
                            "Download PDF"
                        )
                    ) +

                    "</button>" +

                    '<div class="VAS-gljr-detail-actions">' +

                    '<button type="button" ' +
                    'class="VAS-gljr-detail-secondary VAS-gljr-action-approve">' +

                    esc(
                        lbl(
                            "VAS_041_Approve",
                            "Approve"
                        )
                    ) +

                    "</button>" +

                    '<button type="button" ' +
                    'class="VAS-gljr-detail-primary VAS-gljr-action-post">' +

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

                $detailBody =
                    $detailDialog.find(
                        "#VAS-gljr-detail-body-" +
                        id
                    );

                $detailBusy =
                    $detailDialog.find(
                        ".VAS-gljr-detail-busy"
                    );

                $approveButton =
                    $detailDialog.find(
                        ".VAS-gljr-action-approve"
                    );

                $postButton =
                    $detailDialog.find(
                        ".VAS-gljr-action-post"
                    );

                $downloadButton =
                    $detailDialog.find(
                        ".VAS-gljr-download"
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
                    function () {
                        printCurrentPopup();
                    }
                );

                $detailDialog.find(
                    ".VAS-gljr-detail-close-x, " +
                    ".VAS-gljr-detail-scrim"
                ).on(
                    "click",
                    closeDetailDialog
                );

                $(document).on(
                    "keydown.VAS-gljr-" +
                    id,

                    function (event) {
                        if (
                            event.key === "Escape" &&
                            $detailDialog &&
                            $detailDialog.is(
                                ":visible"
                            ) &&
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
                journalId = parseInt(
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
                selectedJournalPosted =
                    false;

                detailLoaded = false;

                updateActionButtons();

                $detailDialog.show();

                $("body").addClass(
                    "VAS-gljr-body-lock"
                );

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
                selectedJournalPosted =
                    false;

                detailLoaded = false;

                updateActionButtons();

                $detailDialog.hide();

                $("body").removeClass(
                    "VAS-gljr-body-lock"
                );
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
                        "VAS/VAS_044_GLJournalRecentWidget/GetJournalEntryDetail",

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
                            !data.error
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

            function isPosted(value) {
                if (
                    value === true ||
                    value === 1
                ) {
                    return true;
                }

                var text = String(
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

            function renderJournalDetail(
                data
            ) {
                var journal =
                    data.Journal || {};

                var lines =
                    data.Lines || [];

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

                var pillClass =
                    PILL_CLASS[
                        selectedJournalStatus
                    ] ||
                    "VAS-gljr-pill-draft";

                var statusText =
                    journal.DocStatus ||
                    journal.StatusName ||
                    selectedJournalStatus ||
                    "";

                var id =
                    $self.AD_UserHomeWidgetID;

                $detailDialog.find(
                    "#VAS-gljr-detail-title-" +
                    id
                ).text(
                    (journal.DocumentNo || "") +
                    " · " +
                    (journal.Description || "")
                );

                $detailDialog.find(
                    "#VAS-gljr-detail-sub-" +
                    id
                ).text(
                    statusText +
                    " · " +
                    (journal.DateAcct || "")
                );

                var totalDebit =
                    symbol +
                    fmtAmt(
                        journal.TotalDebit,
                        precision
                    );

                var totalCredit =
                    symbol +
                    fmtAmt(
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
                    '<div class="VAS-gljr-detail-summary">' +

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

                    '<span class="VAS-gljr-pill ' +
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
                    esc(accountingBook) +
                    "</strong>" +
                    "</div>" +

                    "<div>" +
                    "<span>Total Debit</span>" +
                    "<strong>" +
                    esc(totalDebit) +
                    "</strong>" +
                    "</div>" +

                    "<div>" +
                    "<span>Total Credit</span>" +
                    "<strong>" +
                    esc(totalCredit) +
                    "</strong>" +
                    "</div>" +

                    '<div class="VAS-gljr-detail-description">' +
                    "<span>Description</span>" +
                    "<strong>" +
                    esc(
                        journal.Description
                    ) +
                    "</strong>" +
                    "</div>" +

                    "</div>" +

                    '<div class="VAS-gljr-detail-section-title">' +
                    "Journal Lines" +
                    "</div>" +

                    '<div class="VAS-gljr-detail-lines-wrap">' +

                    '<table class="VAS-gljr-detail-lines">' +

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

                if (
                    !Array.isArray(lines) ||
                    lines.length === 0
                ) {
                    html +=
                        "<tr>" +
                        '<td colspan="7">No journal lines.</td>' +
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
                            esc(accountText) +
                            "</td>" +

                            '<td class="VAS-gljr-detail-amt">' +
                            esc(
                                Number(
                                    line.Debit || 0
                                ) > 0
                                    ? (
                                        symbol +
                                        fmtAmt(
                                            line.Debit,
                                            precision
                                        )
                                    )
                                    : "-"
                            ) +
                            "</td>" +

                            '<td class="VAS-gljr-detail-amt">' +
                            esc(
                                Number(
                                    line.Credit || 0
                                ) > 0
                                    ? (
                                        symbol +
                                        fmtAmt(
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

                    '<td class="VAS-gljr-detail-amt">' +
                    esc(totalDebit) +
                    "</td>" +

                    '<td class="VAS-gljr-detail-amt">' +
                    esc(totalCredit) +
                    "</td>" +

                    '<td colspan="4"></td>' +

                    "</tr>" +
                    "</tfoot>" +

                    "</table>" +
                    "</div>" +

                    '<div class="VAS-gljr-created-strip">' +

                    '<span class="VAS-gljr-avatar">' +
                    esc(
                        initials(
                            journal
                                .CreatedByName
                        )
                    ) +
                    "</span>" +

                    "<div>" +

                    "<span>Created By</span>" +

                    "<strong>" +
                    esc(
                        journal
                            .CreatedByName ||
                        "-"
                    ) +
                    "</strong>" +

                    (
                        journal.CreatedDate
                            ? (
                                " · drafted " +
                                esc(
                                    journal
                                        .CreatedDate
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
                var status = String(
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
                        "Journal details are not loaded."
                    );

                    return;
                }

                /*
                 * Clone the currently displayed popup.
                 * No journal content is rebuilt.
                 */
                var $popupCopy =
                    $detailDialog
                        .find(
                            ".VAS-gljr-detail-card"
                        )
                        .first()
                        .clone();

                if (
                    !$popupCopy ||
                    !$popupCopy.length
                ) {
                    showProcessError(
                        "Journal details are not available."
                    );

                    return;
                }

                /*
                 * Remove controls from the printed copy only.
                 */
                $popupCopy.find(
                    ".VAS-gljr-detail-footer"
                ).remove();

                $popupCopy.find(
                    ".VAS-gljr-detail-close-x"
                ).remove();

                $popupCopy.find(
                    ".VAS-gljr-detail-busy"
                ).remove();

                /*
                 * Remove scrolling restrictions from the copied popup
                 * so all journal lines appear in print.
                 */
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
                    ".VAS-gljr-detail-body"
                ).css({
                    "height": "auto",
                    "max-height": "none",
                    "overflow": "visible"
                });

                $popupCopy.find(
                    ".VAS-gljr-detail-content"
                ).css({
                    "height": "auto",
                    "max-height": "none",
                    "overflow": "visible"
                });

                $popupCopy.find(
                    ".VAS-gljr-detail-lines-wrap"
                ).css({
                    "height": "auto",
                    "max-height": "none",
                    "overflow": "visible"
                });

                /*
                 * Hidden iframe keeps the current screen and popup open.
                 */
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

                /*
                 * Copy all currently loaded stylesheets and style blocks.
                 */
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
                        ".VAS-gljr-detail-title"
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

                    ".VAS-gljr-print-dialog {" +
                    "position: static !important;" +
                    "display: block !important;" +
                    "width: 100% !important;" +
                    "height: auto !important;" +
                    "min-height: 0 !important;" +
                    "padding: 0 !important;" +
                    "margin: 0 !important;" +
                    "overflow: visible !important;" +
                    "background: #ffffff !important;" +
                    "}" +

                    ".VAS-gljr-print-dialog " +
                    ".VAS-gljr-detail-card {" +
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

                    ".VAS-gljr-print-dialog " +
                    ".VAS-gljr-detail-body," +

                    ".VAS-gljr-print-dialog " +
                    ".VAS-gljr-detail-content," +

                    ".VAS-gljr-print-dialog " +
                    ".VAS-gljr-detail-lines-wrap {" +
                    "display: block !important;" +
                    "height: auto !important;" +
                    "max-height: none !important;" +
                    "overflow: visible !important;" +
                    "}" +

                    ".VAS-gljr-print-dialog " +
                    ".VAS-gljr-detail-lines {" +
                    "width: 100% !important;" +
                    "table-layout: auto !important;" +
                    "}" +

                    ".VAS-gljr-print-dialog " +
                    ".VAS-gljr-detail-scrim," +

                    ".VAS-gljr-print-dialog " +
                    ".VAS-gljr-detail-footer," +

                    ".VAS-gljr-print-dialog " +
                    ".VAS-gljr-detail-close-x," +

                    ".VAS-gljr-print-dialog " +
                    ".VAS-gljr-detail-busy {" +
                    "display: none !important;" +
                    "}" +

                    "thead {" +
                    "display: table-header-group !important;" +
                    "}" +

                    "tfoot {" +
                    "display: table-footer-group !important;" +
                    "}" +

                    "tr," +
                    ".VAS-gljr-detail-summary > div," +
                    ".VAS-gljr-created-strip {" +
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

                    "<div class='VAS-gljr-detail-dialog " +
                    "VAS-gljr-print-dialog'>" +

                    $popupCopy[0].outerHTML +

                    "</div>" +

                    "</body>" +
                    "</html>"
                );

                printDocument.close();

                var cleaned = false;

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
                        700
                    );
                }

                printWindow.onafterprint =
                    cleanupPrintFrame;

                /*
                 * Give external CSS files enough time to load.
                 */
                window.setTimeout(
                    function () {
                        try {
                            printWindow.focus();
                            printWindow.print();
                        }
                        catch (error) {
                            cleanupPrintFrame();

                            showProcessError(
                                "Could not open the print window."
                            );
                        }

                        /*
                         * Fallback for browsers that do not fire afterprint.
                         */
                        window.setTimeout(
                            cleanupPrintFrame,
                            5000
                        );
                    },
                    900
                );
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

                var status = String(
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

                journalActionInProgress =
                    true;

                setActionBusy(
                    true,
                    actionType
                );

                actionRequest = $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_044_GLJournalRecentWidget/" +
                        actionName,

                    type: "POST",
                    dataType: "json",
                    cache: false,

                    data: {
                        journalId:
                            selectedJournalId
                    },

                    success: function (
                        result
                    ) {
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
                                "Journal process failed."
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

                        journalActionInProgress =
                            false;

                        setActionBusy(
                            false,
                            actionType
                        );

                        loadData();

                        /*
                         * Recent widget keeps the record visible.
                         * Reload details and update action buttons.
                         */
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
                            "Journal process failed."
                        );
                    },

                    complete: function () {
                        actionRequest = null;

                        if (
                            journalActionInProgress
                        ) {
                            journalActionInProgress =
                                false;

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
                            ? "Approving..."
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
                            ? "Posting..."
                            : lbl(
                                "VAS_041_PostJournal",
                                "Post journal"
                            )
                    );
                }

                updateActionButtons();
            }

            function showProcessError(
                message
            ) {
                window.alert(
                    message ||
                    "Journal process failed."
                );
            }

            function initials(name) {
                var parts = String(
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

            function renderDetailError(
                message
            ) {
                detailLoaded = false;

                $detailBody.html(
                    '<div class="VAS-gljr-detail-empty">' +
                    esc(
                        message ||
                        lbl(
                            "VIS_Error",
                            "Error loading data."
                        )
                    ) +
                    "</div>"
                );

                selectedJournalStatus = "";
                selectedJournalPosted =
                    false;

                updateActionButtons();
            }

            function showEmpty(message) {
                var id =
                    $self.AD_UserHomeWidgetID;

                $root.find(
                    ".VAS-gljr-table-wrap"
                ).addClass(
                    "is-empty"
                );

                $root.find(
                    "#VAS-gljr-tbody-" +
                    id
                ).html(
                    '<tr class="VAS-gljr-empty-row">' +
                    '<td colspan="6" class="VAS-gljr-empty">' +
                    esc(
                        message ||
                        lbl(
                            "VIS_Error",
                            "Error loading data."
                        )
                    ) +
                    "</td>" +
                    "</tr>"
                );

                totalPages = 0;
                updatePager();
            }

            this.refreshData = function () {
                showBusy(true);
                loadData();
            };

            this.getRoot = function () {
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
                        loadRequest &&
                        loadRequest.readyState !== 4
                    ) {
                        loadRequest.abort();
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
                        "keydown.VAS-gljr-" +
                        $self.AD_UserHomeWidgetID
                    );

                    $("body").removeClass(
                        "VAS-gljr-body-lock"
                    );

                    if ($detailDialog) {
                        $detailDialog.remove();
                        $detailDialog = null;
                    }

                    $root.off();
                    $root.remove();

                    $detailBody = null;
                    $detailBusy = null;

                    $approveButton = null;
                    $postButton = null;
                    $downloadButton = null;

                    currentData = null;

                    selectedJournalId = 0;
                    selectedJournalStatus = "";
                    selectedJournalPosted =
                        false;

                    detailLoaded = false;
                };
        };

    VAS.VAS_044_GLJournalRecentWidget
        .prototype.refreshWidget =
        function () {
            if (
                typeof this.refreshData ===
                "function"
            ) {
                this.refreshData();
            }
        };

    VAS.VAS_044_GLJournalRecentWidget
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

    VAS.VAS_044_GLJournalRecentWidget
        .prototype.widgetSizeChange =
        function (
            height,
            width
        ) {
        };

    VAS.VAS_044_GLJournalRecentWidget
        .prototype.dispose =
        function () {
            this.disposeComponent();

            if (this.frame) {
                this.frame.dispose();
            }

            this.frame = null;
        };

})(VAS, jQuery);


