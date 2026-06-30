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
 * VAS_044_JournalLines                 Journal Lines                        س�\u00B7ور القيد
 * VAS_044_Account                      Account                              الحساب
 * VAS_044_CostCenter                   Cost Center                          مركز التكلفة
 * VAS_044_BusinessPartner              Business Partner                     شريك العمل
 * VAS_044_Product                      Product                              المنتج
 * VAS_044_Project                      Project                              المشروع
 * VAS_044_Total                        Total                                الإجمالي
 * VAS_044_CreatedBy                    Created By                           تم الإنشاء بواس�\u00B7ة
 * VAS_044_Drafted                      Drafted                              تم إنشاء المسودة
 * VAS_044_NoJournalLines               No journal lines.                    لا توجد س�\u00B7ور للقيد
 * VAS_044_Approving                    Approving...                         جارٍ تنفيذ الموافقة...
 * VAS_044_Posting                      Posting...                           جارٍ ترحيل القيد...
 * VAS_044_JournalProcessFailed         Journal process failed.              فشلت معالجة القيد
 * VAS_044_DetailsNotLoaded             Journal details are not loaded.      لم يتم تحميل تفاصيل القيد
 * VAS_044_DetailsNotAvailable          Journal details are not available.   تفاصيل القيد غير متوفرة
 * VAS_044_PrintWindowFailed            Could not open the print window.     تعذر فتح نافذة ال�\u00B7باعة
 * VAS_044_LoadFailed                   Could Not Load Recent Entries        
 * VAS_044_InvalidJournalID             Invalid Journal ID                   
 * VAS_044_DetailsLoadFailed            Could Not Load Journal Details       
 *
 * Shared Messages:
 *
 * Value                                English Text                          Arabic Text
 * ---------------------------------------------------------------------------------------------------------
 * VAS_DownloadPDF                      Download PDF                         تنزيل PDF
 * VAS_044_Approve                      Approve                              موافقة
 * VAS_044_PostJournal                  Post journal                         ترحيل القيد
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
 *
 * Purpose:
 * Displays the most recent GL journal entries and opens
 * the selected journal details inside a popup.
 *
 * Status response:
 *
 * Status: {
 *     Value: "DR",
 *     Name: "Drafted"
 * }
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    function lbl(key, fallback) {
        var text =
            VIS.Msg.getMsg(key);

        return (
            text &&
            text !== "[" + key + "]"
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
                typeof data !== "string"
            ) {
                break;
            }

            try {
                data =
                    JSON.parse(data);
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
            Number(precision);

        if (
            isNaN(resolvedPrecision) ||
            resolvedPrecision < 0
        ) {
            resolvedPrecision =
                standardPrecision;
        }

        if (
            isNaN(resolvedPrecision) ||
            resolvedPrecision < 0
        ) {
            resolvedPrecision =
                2;
        }

        var numericAmount =
            Number(
                amount || 0
            );

        if (isNaN(numericAmount)) {
            numericAmount =
                0;
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

    /*
     * Supports the new Status object and the old fields.
     */
    function getStatusValue(source) {
        source =
            source || {};

        if (
            source.Status &&
            source.Status.Value != null
        ) {
            return String(
                source.Status.Value
            );
        }

        return String(
            source.StatusValue ||
            source.DocStatus ||
            ""
        );
    }

    function getStatusName(source) {
        source =
            source || {};

        if (
            source.Status &&
            source.Status.Name
        ) {
            return String(
                source.Status.Name
            );
        }

        return String(
            source.StatusName ||
            getStatusValue(source) ||
            ""
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
                lbl(
                    "VIS_Error",
                    "Error loading data."
                )
            );
        }

        if (
            xhr &&
            xhr.status
        ) {
            return (
                fallback ||
                lbl(
                    "VIS_Error",
                    "Error loading data."
                )
            ) +
                " HTTP " +
                xhr.status;
        }

        return (
            fallback ||
            lbl(
                "VIS_Error",
                "Error loading data."
            )
        );
    }

    var PILL_CLASS = {
        "DR":
            "VAS-gljr-pill-draft",

        "CO":
            "VAS-gljr-pill-posted",

        "IP":
            "VAS-gljr-pill-submit",

        "AP":
            "VAS-gljr-pill-posted",

        "NA":
            "VAS-gljr-pill-pending",

        "VO":
            "VAS-gljr-pill-voided",

        "RE":
            "VAS-gljr-pill-reverse",

        "CL":
            "VAS-gljr-pill-closed"
    };

    VAS.VAS_044_GLJournalRecentWidget =
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
                    '<div class="VAS-gljr-root">'
                );

            var $detailDialog =
                null;

            var $detailBody =
                null;

            var $detailBusy =
                null;

            var $postButton =
                null;

            var $downloadButton =
                null;

            var currentData =
                null;

            var pageNo =
                1;

            var pageSize =
                3;

            var totalPages =
                0;

            var selectedJournalId =
                0;

            var selectedJournalStatus =
                "";

            var selectedJournalPosted =
                false;

            var detailLoaded =
                false;

            var detailLinePageNo =
                1;

            var detailLinePageSize =
                3;

            var detailLineTotalPages =
                1;

            var journalActionInProgress =
                false;

            var refreshTimer =
                null;

            var loadRequest =
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

            function documentIconSvg() {
                return (
                    '<svg viewBox="0 0 24 24" ' +
                    'fill="none" ' +
                    'stroke="currentColor" ' +
                    'stroke-width="2" ' +
                    'stroke-linecap="round" ' +
                    'stroke-linejoin="round">' +

                    '<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"></path>' +

                    '<polyline points="14 2 14 8 20 8"></polyline>' +

                    "</svg>"
                );
            }

            function createBusyIndicator() {
                var $busy =
                    $(
                        '<div id="VAS-gljr-busy-' +
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
                var id =
                    $self.AD_UserHomeWidgetID;

                var documentIcon =
                    documentIconSvg();

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

                    '<tr>' +

                    '<td colspan="6" class="VAS-gljr-empty">' +

                    "&mdash;" +

                    "</td>" +

                    "</tr>" +

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

                    '">' +

                    "&#8249;" +

                    "</button>" +

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

                    '">' +

                    "&#8250;" +

                    "</button>" +

                    "</div>" +

                    "</div>";

                $root.append(
                    html
                );

                /*
                 * Delegated event.
                 * It remains active after tbody HTML is rebuilt.
                 */
                $root.on(
                    "click.VAS044Row",
                    ".VAS-gljr-row",
                    function (event) {
                        event.preventDefault();
                        event.stopPropagation();

                        var journalId =
                            parseInt(
                                $(this).attr(
                                    "data-id"
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

                $root.on(
                    "keydown.VAS044Row",
                    ".VAS-gljr-row",
                    function (event) {
                        if (
                            event.key !== "Enter" &&
                            event.key !== " "
                        ) {
                            return;
                        }

                        event.preventDefault();
                        event.stopPropagation();

                        var journalId =
                            parseInt(
                                $(this).attr(
                                    "data-id"
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

                $root.on(
                    "click.VAS044Pager",
                    ".VAS-gljr-prev",
                    function () {
                        if (pageNo <= 1) {
                            return;
                        }

                        pageNo--;

                        loadData();
                    }
                );

                $root.on(
                    "click.VAS044Pager",
                    ".VAS-gljr-next",
                    function () {
                        if (
                            totalPages <= 1 ||
                            pageNo >= totalPages
                        ) {
                            return;
                        }

                        pageNo++;

                        loadData();
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

                loadRequest =
                    $.ajax({
                        url:
                            baseUrl +
                            "VAS/VAS_044_GLJournalRecentWidget/GetRecentEntries",

                        type:
                            "GET",

                        data:
                            {
                                pageNo:
                                    pageNo,

                                pageSize:
                                    pageSize
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

                                currentData =
                                    data;

                                renderRows(
                                    data,
                                    $self
                                        .AD_UserHomeWidgetID
                                );
                            },

                        error:
                            function (
                                xhr,
                                textStatus
                            ) {
                                if (
                                    isDisposed ||
                                    textStatus === "abort"
                                ) {
                                    return;
                                }

                                showEmpty(
                                    getAjaxErrorMessage(
                                        xhr,
                                        lbl(
                                            "VAS_044_LoadFailed",
                                            "Error loading journal entries."
                                        )
                                    )
                                );
                            },

                        complete:
                            function () {
                                loadRequest =
                                    null;

                                if (!isDisposed) {
                                    showBusy(false);
                                }
                            }
                    });
            }

            function renderRows(
                data,
                id
            ) {
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

                if (!entries.length) {
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

                    totalPages =
                        0;

                    updatePager();

                    return;
                }

                $root.find(
                    ".VAS-gljr-table-wrap"
                ).removeClass(
                    "is-empty"
                );

                totalPages =
                    Number(
                        data.TotalPages ||
                        Math.ceil(
                            Number(
                                data.TotalCount ||
                                entries.length ||
                                0
                            ) /
                            pageSize
                        ) ||
                        1
                    );

                pageSize =
                    Number(
                        data.PageSize ||
                        pageSize ||
                        5
                    );

                pageNo =
                    Number(
                        data.PageNo ||
                        pageNo ||
                        1
                    );

                var html =
                    "";

                for (
                    var index = 0;
                    index < entries.length;
                    index++
                ) {
                    var entry =
                        entries[index] || {};

                    var journalId =
                        parseInt(
                            entry.GL_Journal_ID,
                            10
                        );

                    if (
                        isNaN(journalId) ||
                        journalId <= 0
                    ) {
                        continue;
                    }

                    /*
                     * Value controls the CSS.
                     * Name is displayed to the user.
                     */
                    var statusValue =
                        getStatusValue(
                            entry
                        ).toUpperCase();

                    var statusName =
                        getStatusName(
                            entry
                        );

                    var pillClass =
                        PILL_CLASS[
                        statusValue
                        ] ||
                        "VAS-gljr-pill-draft";

                    var debitText =
                        Number(
                            entry.TotalDebit || 0
                        ) > 0
                            ? (
                                symbol +
                                formatAmount(
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
                                formatAmount(
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

                    var rowInformation =
                        esc(
                            (
                                entry.DocumentNo ||
                                ""
                            ) +

                            " - " +

                            statusName +

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
                        '" ' +

                        'data-id="' +
                        journalId +
                        '" ' +

                        'role="button" ' +

                        'tabindex="0" ' +

                        'title="' +
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
                        '" ' +

                        'data-status-value="' +
                        esc(
                            statusValue
                        ) +
                        '">' +

                        esc(
                            statusName
                        ) +

                        "</span>" +

                        "</td>" +

                        '<td class="VAS-gljr-col-num">' +
                        esc(
                            debitText
                        ) +
                        "</td>" +

                        '<td class="VAS-gljr-col-num">' +
                        esc(
                            creditText
                        ) +
                        "</td>" +

                        "</tr>";
                }

                $tableBody.html(
                    html
                );

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
                    $pageText.text(
                        ""
                    );
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

                $detailDialog =
                    $(
                        '<div class="VAS-gljr-detail-dialog" ' +

                        'id="VAS-gljr-detail-dialog-' +
                        id +
                        '" ' +

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
                        '">' +

                        "&mdash;" +

                        "</div>" +

                        '<div class="VAS-gljr-detail-sub" ' +

                        'id="VAS-gljr-detail-sub-' +
                        id +
                        '">' +

                        "&mdash;" +

                        "</div>" +

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

                        'class="VAS-gljr-detail-primary VAS-gljr-action-post">' +

                        esc(
                            lbl(
                                "VAS_044_PostJournal",
                                "Post Journal"
                            )
                        ) +

                        "</button>" +

                        '<button type="button" ' +

                        'class="VAS-gljr-detail-primary VAS-gljr-detail-close">' +

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
                        "#VAS-gljr-detail-body-" +
                        id
                    );

                $detailBusy =
                    $detailDialog.find(
                        ".VAS-gljr-detail-busy"
                    );

                $postButton =
                    $detailDialog.find(
                        ".VAS-gljr-action-post"
                    );

                $downloadButton =
                    $detailDialog.find(
                        ".VAS-gljr-download"
                    );

                showDetailBusy(
                    false
                );

                updateActionButtons();

                $postButton.on(
                    "click.VAS044Post",
                    function () {
                        executeJournalAction(
                            "PostJournal",
                            "post"
                        );
                    }
                );

                $downloadButton.on(
                    "click.VAS044Print",
                    printCurrentPopup
                );

                $detailDialog.find(
                    ".VAS-gljr-detail-close-x, " +
                    ".VAS-gljr-detail-close, " +
                    ".VAS-gljr-detail-scrim"
                ).on(
                    "click.VAS044Close",
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

                $detailDialog.hide();
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

                selectedJournalStatus =
                    "";

                selectedJournalPosted =
                    false;

                detailLoaded =
                    false;

                $detailDialog.find(
                    "#VAS-gljr-detail-title-" +
                    $self.AD_UserHomeWidgetID
                ).html(
                    "&mdash;"
                );

                $detailDialog.find(
                    "#VAS-gljr-detail-sub-" +
                    $self.AD_UserHomeWidgetID
                ).html(
                    "&mdash;"
                );

                $detailBody.empty();

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

                selectedJournalId =
                    0;

                selectedJournalStatus =
                    "";

                selectedJournalPosted =
                    false;

                detailLoaded =
                    false;

                detailLinePageNo =
                    1;

                detailLineTotalPages =
                    1;

                updateActionButtons();

                $detailDialog.hide();

                $("body").removeClass(
                    "VAS-gljr-body-lock"
                );
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
                        lbl(
                            "VAS_044_InvalidJournalID",
                            "Invalid journal ID."
                        )
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
                        ".VAS044LinePager"
                    )
                    .on(
                        "click.VAS044LinePager",
                        ".VAS-gljr-line-prev, .VAS-gljr-line-next",
                        function () {
                            if (
                                journalActionInProgress ||
                                selectedJournalId <= 0
                            ) {
                                return;
                            }

                            var nextPage =
                                $(this).hasClass(
                                    "VAS-gljr-line-prev"
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
                                    data.success !== false &&
                                    !data.error &&
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
                                            : lbl(
                                                "VAS_044_DetailsNotAvailable",
                                                "Journal details are not available."
                                            )
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
                                    textStatus === "abort"
                                ) {
                                    return;
                                }

                                var message =
                                    getAjaxErrorMessage(
                                        xhr,
                                        errorThrown ||
                                        lbl(
                                            "VAS_044_DetailsLoadFailed",
                                            "Error loading journal details."
                                        )
                                    );

                                if (
                                    window.console &&
                                    console.error
                                ) {
                                    console.error(
                                        "GetJournalEntryDetail failed",
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
                    data.Journal || {};

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
                    !isNaN(returnedJournalId) &&
                    returnedJournalId > 0
                ) {
                    selectedJournalId =
                        returnedJournalId;
                }

                /*
                 * Value is used for actions and CSS.
                 * Name is displayed in the popup.
                 */
                selectedJournalStatus =
                    getStatusValue(
                        journal
                    ).toUpperCase();

                selectedJournalPosted =
                    isPosted(
                        journal.Posted
                    );

                var statusName =
                    getStatusName(
                        journal
                    );

                var pillClass =
                    PILL_CLASS[
                    selectedJournalStatus
                    ] ||
                    "VAS-gljr-pill-draft";

                var id =
                    $self.AD_UserHomeWidgetID;

                $detailDialog.find(
                    "#VAS-gljr-detail-title-" +
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
                    "#VAS-gljr-detail-sub-" +
                    id
                ).text(
                    statusName +

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

                var totalCredit =
                    symbol +
                    formatAmount(
                        journal.TotalCredit,
                        precision
                    );

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
                        " \u00B7 " +
                        data.ISOCode;
                }

                var html =
                    '<div class="VAS-gljr-detail-summary">' +

                    "<div>" +

                    "<span>" +

                    esc(
                        lbl(
                            "VAS_044_JournalNo",
                            "Journal No."
                        )
                    ) +

                    "</span>" +

                    "<strong>" +

                    esc(
                        journal.DocumentNo
                    ) +

                    "</strong>" +

                    "</div>" +

                    "<div>" +

                    "<span>" +

                    esc(
                        lbl(
                            "VAS_044_Date",
                            "Date"
                        )
                    ) +

                    "</span>" +

                    "<strong>" +

                    esc(
                        journal.DateAcct
                    ) +

                    "</strong>" +

                    "</div>" +

                    "<div>" +

                    "<span>" +

                    esc(
                        lbl(
                            "VAS_044_Status",
                            "Status"
                        )
                    ) +

                    "</span>" +

                    "<strong>" +

                    '<span class="VAS-gljr-pill ' +
                    pillClass +
                    '" ' +

                    'data-status-value="' +

                    esc(
                        selectedJournalStatus
                    ) +

                    '">' +

                    esc(
                        statusName
                    ) +

                    "</span>" +

                    "</strong>" +

                    "</div>" +

                    "<div>" +

                    "<span>" +

                    esc(
                        lbl(
                            "VAS_044_AccountingBook",
                            "Accounting Book"
                        )
                    ) +

                    "</span>" +

                    "<strong>" +

                    esc(
                        accountingBook
                    ) +

                    "</strong>" +

                    "</div>" +

                    "<div>" +

                    "<span>" +

                    esc(
                        lbl(
                            "VAS_044_Currency",
                            "Currency"
                        )
                    ) +

                    "</span>" +

                    "<strong>" +

                    esc(
                        currencyText
                    ) +

                    "</strong>" +

                    "</div>" +

                    "<div>" +

                    "<span>" +

                    esc(
                        lbl(
                            "VAS_044_TotalDebit",
                            "Total Debit"
                        )
                    ) +

                    "</span>" +

                    "<strong>" +

                    esc(
                        totalDebitAmt
                    ) +

                    "</strong>" +

                    "</div>" +

                    "<div>" +

                    "<span>" +

                    esc(
                        lbl(
                            "VAS_044_TotalCredit",
                            "Total Credit"
                        )
                    ) +

                    "</span>" +

                    "<strong>" +

                    esc(
                        totalCreditAmt
                    ) +

                    "</strong>" +

                    "</div>" +

                    "<div>" +

                    "<span>" +

                    esc(
                        lbl(
                            "VAS_044_Description",
                            "Description"
                        )
                    ) +

                    "</span>" +

                    "<strong>" +

                    esc(
                        journal.Description
                    ) +

                    "</strong>" +

                    "</div>" +

                    "</div>" +

                    '<div class="VAS-gljr-detail-section-title">' +

                    esc(
                        lbl(
                            "VAS_044_JournalLines",
                            "Journal Lines"
                        )
                    ) +

                    "</div>" +

                    '<div class="VAS-gljr-detail-lines-wrap">' +

                    '<table class="VAS-gljr-detail-lines">' +

                    "<thead>" +

                    "<tr>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Account",
                            "Account"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Debit",
                            "Debit"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Credit",
                            "Credit"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_CostCenter",
                            "Cost Center"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_BusinessPartner",
                            "Business Partner"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Product",
                            "Product"
                        )
                    ) +
                    "</th>" +

                    "<th>" +
                    esc(
                        lbl(
                            "VAS_044_Project",
                            "Project"
                        )
                    ) +
                    "</th>" +

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
                        var lineIndex = 0;
                        lineIndex < lines.length;
                        lineIndex++
                    ) {
                        var line =
                            lines[lineIndex] || {};

                        var accountText =
                            line.AccountCode &&
                                line.AccountName
                                ? (
                                    line.AccountCode +
                                    " \u00B7 " +
                                    line.AccountName
                                )
                                : (
                                    line.AccountCode ||
                                    line.AccountName ||
                                    "-"
                                );

                        html +=
                            "<tr>" +

                            '<td title="' + esc(accountText) + '">' +
                            esc(accountText) +
                            "</td>" +

                            (function () {
                                var ld = Number(line.Debit || 0) > 0 ? formatAmount(line.Debit, precision) : "-";
                                var lc = Number(line.Credit || 0) > 0 ? formatAmount(line.Credit, precision) : "-";
                                return '<td class="VAS-gljr-num" title="' + esc(ld) + '">' + esc(ld) + "</td>" +
                                       '<td class="VAS-gljr-num" title="' + esc(lc) + '">' + esc(lc) + "</td>";
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

                    "<td>" +

                    esc(
                        lbl(
                            "VAS_044_Total",
                            "Total"
                        )
                    ) +

                    "</td>" +

                    '<td class="VAS-gljr-num" title="' + esc(totalDebitAmt) + '">' +

                    esc(
                        totalDebitAmt
                    ) +

                    "</td>" +

                    '<td class="VAS-gljr-num" title="' + esc(totalCreditAmt) + '">' +

                    esc(
                        totalCreditAmt
                    ) +

                    "</td>" +

                    '<td colspan="4"></td>' +

                    "</tr>" +

                    "</tfoot>" +

                    "</table>" +

                    "</div>" +

                    '<div class="VAS-gljr-line-pager">' +

                    '<button type="button" ' +
                    'class="VAS-gljr-page-btn VAS-gljr-line-prev" ' +
                    (
                        detailLinePageNo <= 1 ||
                        detailLineTotalPages <= 1
                            ? "disabled "
                            : ""
                    ) +
                    'aria-label="Previous">&#8249;</button>' +

                    '<span class="VAS-gljr-page-text">' +
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
                    'class="VAS-gljr-page-btn VAS-gljr-line-next" ' +
                    (
                        detailLinePageNo >= detailLineTotalPages ||
                        detailLineTotalPages <= 1
                            ? "disabled "
                            : ""
                    ) +
                    'aria-label="Next">&#8250;</button>' +

                    "</div>" +

                    '<div class="VAS-gljr-created-strip">' +

                    '<span class="VAS-gljr-avatar">' +

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
                            "VAS_044_CreatedBy",
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
                                " \u00B7 " +

                                esc(
                                    lbl(
                                        "VAS_044_Drafted",
                                        "Drafted"
                                    )
                                ) +

                                " " +

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

                var canPost =
                    detailLoaded &&
                    !selectedJournalPosted &&
                    (
                        status === "AP" ||
                        status === "CO" ||
                        status === "CL"
                    );

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
                            "VAS/VAS_044_GLJournalRecentWidget/" +
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

                                loadData();

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
                                    textStatus === "abort"
                                ) {
                                    return;
                                }

                                showProcessError(
                                    getAjaxErrorMessage(
                                        xhr,
                                        lbl(
                                            "VAS_044_JournalProcessFailed",
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

                if ($postButton) {
                    $postButton.text(
                        busy &&
                            actionType === "post"
                            ? lbl(
                                "VAS_044_Posting",
                                "Posting..."
                            )
                            : lbl(
                                "VAS_044_PostJournal",
                                "Post Journal"
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

                journalActionInProgress =
                    true;

                showDetailBusy(
                    true
                );

                updateActionButtons();

                $.ajax({
                    url:
                        baseUrl +
                        "VAS/VAS_044_GLJournalRecentWidget/" +
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
                                        "VAS_044_PrintProcessNotFound",
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
                                        "VAS_044_PrintProcessNotFound",
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
                                        "VAS_044_PrintFailed",
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
                            showProcessError(
                                getAjaxErrorMessage(
                                    xhr,
                                    lbl(
                                        "VAS_044_PrintFailed",
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

            function showEmpty(message) {
                var $tableBody =
                    $root.find(
                        "#VAS-gljr-tbody-" +
                        $self.AD_UserHomeWidgetID
                    );

                currentData =
                    null;

                pageNo =
                    1;

                totalPages =
                    0;

                $root.find(
                    ".VAS-gljr-table-wrap"
                ).addClass(
                    "is-empty"
                );

                $tableBody.html(
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

                updatePager();
            }

            function renderDetailError(
                message
            ) {
                detailLoaded =
                    false;

                selectedJournalStatus =
                    "";

                selectedJournalPosted =
                    false;

                $detailBody.html(
                    '<div class="VAS-gljr-empty">' +

                    esc(
                        message ||

                        lbl(
                            "VAS_044_DetailsLoadFailed",
                            "Error loading journal details."
                        )
                    ) +

                    "</div>"
                );

                updateActionButtons();
            }

            function refreshData() {
                pageNo =
                    1;

                currentData =
                    null;

                loadData();

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
                        $detailDialog.off();

                        $detailDialog.remove();

                        $detailDialog =
                            null;
                    }

                    $root.off();

                    $root.remove();

                    $detailBody =
                        null;

                    $detailBusy =
                        null;

                    $postButton =
                        null;

                    $downloadButton =
                        null;

                    currentData =
                        null;

                    selectedJournalId =
                        0;

                    selectedJournalStatus =
                        "";

                    selectedJournalPosted =
                        false;

                    detailLoaded =
                        false;
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

            this.frame =
                null;
        };

})(VAS, jQuery);
