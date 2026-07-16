/**
 * Shared GL Journal Detail Dialog
 *
 * Module   : VAS - GL Journal widgets
 * Purpose  : A single, reusable "journal detail" modal (the card that was
 *            previously duplicated inside VAS_041 / VAS_044 / VAS_045 / VAS_036).
 *            One dialog instance is built lazily and mounted on <body>; every
 *            GL-journal widget opens the SAME modal through
 *            VAS.GLJournalDetailDialog.open(journalId, opts).
 *
 * All data + actions route through VAS_041's controller, which owns the generic
 * journalId-keyed endpoints:
 *   GetJournalEntryDetail, PostJournal, GetJournalPrintInfo.
 * The markup reuses the VAS-glje-* detail classes, styled by
 * VAS_041_GLJournalEntriesWidget.css (globally bundled via VAScss.css) - so no
 * new CSS ships with this file.
 *
 * The footer shows a Post button (for Approved/Completed/Closed, not-yet-posted
 * journals) and a Close button; Download PDF is opt-in via showDownload.
 *
 * Public API:
 *   VAS.GLJournalDetailDialog.open(journalId, {
 *       windowNo:     <number>,    // WindowNo passed to the PDF print process (default 0)
 *       showDownload: <boolean>,   // show the "Download PDF" button (default false)
 *       onChanged:    function(){} // invoked after a successful post so the
 *                                  // host widget can refresh its own KPI / list
 *   });
 *   VAS.GLJournalDetailDialog.close();
 *   VAS.GLJournalDetailDialog.isBusy();   // true while an approve/post/print is running
 *
 * -- Author placeholder: replace VAS_XXX with your employee code before commit --
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    var CTRL =
        "VAS/VAS_041_GLJournalEntriesWidget/";

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

    /* ---- private state (single shared instance) ------------------------- */

    var $dialog = null;
    var $body = null;
    var $busy = null;
    var $postButton = null;
    var $downloadButton = null;
    var $closeButton = null;

    var built = false;

    var currentOpts = {};

    /* Download PDF is opt-in per host (only some widgets expose it). */
    var showDownload = false;

    var selectedJournalId = 0;
    var selectedJournalStatus = "";
    var selectedJournalPosted = false;
    var detailLoaded = false;
    var actionInProgress = false;

    var linePageNo = 1;
    var linePageSize = 20;
    var lineTotalPages = 1;

    var detailRequest = null;
    var actionRequest = null;

    var prevBodyOverflow = "";
    var bodyLocked = false;

    /* ---- helpers -------------------------------------------------------- */

    function lbl(key, fallback) {
        var text = VIS.Msg.getMsg(key);

        return (text && text.charAt(0) !== "[")
            ? text
            : fallback;
    }

    function esc(value) {
        return String(value == null ? "" : value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#039;");
    }

    function normalizeResponse(result) {
        var data = result;

        if (data && data.d !== undefined) {
            data = data.d;
        }

        for (var index = 0; index < 3; index++) {
            if (typeof data !== "string") {
                break;
            }

            try {
                data = JSON.parse(data);
            }
            catch (error) {
                return null;
            }

            if (data && data.d !== undefined) {
                data = data.d;
            }
        }

        return data;
    }

    function formatAmount(amount, precision) {
        var standardPrecision = 2;

        try {
            standardPrecision =
                Number(VIS.Env.getCtx().getStdPrecision());
        }
        catch (error) {
            standardPrecision = 2;
        }

        var resolvedPrecision = Number(precision);

        if (isNaN(resolvedPrecision) || resolvedPrecision < 0) {
            resolvedPrecision = standardPrecision;
        }

        if (isNaN(resolvedPrecision) || resolvedPrecision < 0) {
            resolvedPrecision = 2;
        }

        var numericAmount = Number(amount || 0);

        if (isNaN(numericAmount)) {
            numericAmount = 0;
        }

        return numericAmount.toLocaleString(
            window.navigator.language,
            {
                minimumFractionDigits: resolvedPrecision,
                maximumFractionDigits: resolvedPrecision
            }
        );
    }

    function formatRangeText(pageNo, pageSize, totalCount) {
        var total = Number(totalCount || 0);

        if (isNaN(total) || total <= 0) {
            return "";
        }

        var page = Math.max(parseInt(pageNo || 1, 10), 1);
        var size = Math.max(parseInt(pageSize || total, 10), 1);

        var start = ((page - 1) * size) + 1;

        if (start > total) {
            start = total;
        }

        var end = Math.min(start + size - 1, total);

        return "Showing " + start + "-" + end + " of " + total;
    }

    function isPosted(value) {
        if (value === true || value === 1) {
            return true;
        }

        var text = String(value == null ? "" : value).toUpperCase();

        return text === "Y" || text === "TRUE" || text === "1";
    }

    function getAjaxErrorMessage(xhr, fallback) {
        var response = normalizeResponse(xhr && xhr.responseText);

        if (response) {
            if (response.errorKey || response.messageKey) {
                return lbl(
                    response.errorKey || response.messageKey,
                    response.errorText ||
                    response.error ||
                    response.message ||
                    fallback ||
                    lbl("VIS_Error", "Error loading data.")
                );
            }

            return (
                response.errorText ||
                response.error ||
                response.message ||
                fallback ||
                lbl("VIS_Error", "Error loading data.")
            );
        }

        if (xhr && xhr.status) {
            return lbl("VAS_041_RequestFailedHttp", "Request failed. HTTP {0}")
                .replace("{0}", xhr.status);
        }

        return fallback || lbl("VIS_Error", "Error loading data.");
    }

    function initials(name) {
        var parts = String(name || "").trim().split(/\s+/);

        if (!parts.length || !parts[0]) {
            return "--";
        }

        if (parts.length === 1) {
            return parts[0].charAt(0).toUpperCase();
        }

        return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
    }

    function docIconSvg() {
        return (
            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
            '<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"></path>' +
            '<polyline points="14 2 14 8 20 8"></polyline>' +
            '<path d="M16 13H8M16 17H8"></path>' +
            "</svg>"
        );
    }

    function showBusy(show) {
        if (!$busy || !$busy[0]) {
            return;
        }

        $busy[0].style.visibility = show ? "visible" : "hidden";
    }

    /* Self-contained scroll lock (inline style, saved/restored). Independent of
       the host list dialog's own body-lock CSS class - clearing our inline
       overflow never removes that class, so a still-open list stays locked. */
    function lockBodyScroll(lock) {
        if (lock) {
            if (bodyLocked) {
                return;
            }

            prevBodyOverflow = document.body.style.overflow || "";
            document.body.style.overflow = "hidden";
            bodyLocked = true;
        }
        else {
            if (!bodyLocked) {
                return;
            }

            document.body.style.overflow = prevBodyOverflow;
            bodyLocked = false;
        }
    }

    function showProcessError(message) {
        window.alert(
            message ||
            lbl("VAS_041_JournalProcessFailed", "Journal process failed.")
        );
    }

    /* Dimension cell: left-aligned with a value, centered "-" placeholder. */
    function dimCell(value) {
        var text =
            (value === undefined || value === null || String(value) === "")
                ? ""
                : String(value);

        return "<td" +
            (text ? "" : ' class="VAS-glje-null"') +
            ">" +
            esc(text || "-") +
            "</td>";
    }

    function buildDetailLineRows(lines, precision) {
        if (!lines.length) {
            return "<tr><td colspan=\"7\">" +
                esc(lbl("VAS_041_NoJournalLines", "No journal lines.")) +
                "</td></tr>";
        }

        var rows = "";

        for (var index = 0; index < lines.length; index++) {
            var line = lines[index] || {};

            var accountText =
                (line.AccountCode && line.AccountName)
                    ? (line.AccountCode + " · " + line.AccountName)
                    : (line.AccountCode || line.AccountName || "-");

            var ld =
                Number(line.Debit || 0) > 0
                    ? formatAmount(line.Debit, precision)
                    : "-";

            var lc =
                Number(line.Credit || 0) > 0
                    ? formatAmount(line.Credit, precision)
                    : "-";

            rows +=
                "<tr>" +
                "<td>" + esc(accountText) + "</td>" +
                '<td class="VAS-glje-amt" title="' + esc(ld) + '">' + esc(ld) + "</td>" +
                '<td class="VAS-glje-amt" title="' + esc(lc) + '">' + esc(lc) + "</td>" +
                dimCell(line.CostCenter) +
                dimCell(line.BPartner) +
                dimCell(line.Product) +
                dimCell(line.Project) +
                "</tr>";
        }

        return rows;
    }

    /* In-place refresh of the line-pager (no DOM rebuild) for line prev/next. */
    function applyLinePagerState(lineCount) {
        if (!$body) {
            return;
        }

        var $pager = $body.find(".VAS-glje-line-pager");

        $pager.find(".VAS-glje-page-text").text(
            lineCount
                ? formatRangeText(linePageNo, linePageSize, lineCount)
                : ""
        );

        $pager.find(".VAS-glje-page-count").text(
            lineCount
                ? (linePageNo + " " + lbl("VIS_Of", "of") + " " + lineTotalPages)
                : ""
        );

        $pager.find(".VAS-glje-line-prev").prop(
            "disabled",
            linePageNo <= 1 || lineTotalPages <= 1
        );

        $pager.find(".VAS-glje-line-next").prop(
            "disabled",
            linePageNo >= lineTotalPages || lineTotalPages <= 1
        );
    }

    /* ---- build (once) --------------------------------------------------- */

    function ensureBuilt() {
        if (built) {
            return;
        }

        var svgIcon = docIconSvg();

        $dialog = $(
            '<div class="VAS-glje-dialog VAS-glje-detail-dialog" ' +
            'style="display:none;z-index:1000002" role="dialog" aria-modal="true">' +

            '<div class="VAS-glje-dialog-scrim"></div>' +

            '<div class="VAS-glje-dialog-card VAS-glje-detail-card">' +

            '<div class="VAS-glje-dialog-head">' +

            '<div class="VAS-glje-dialog-icon">' + svgIcon + "</div>" +

            '<div class="VAS-glje-dialog-title-wrap">' +
            '<div class="VAS-glje-dialog-title">&mdash;</div>' +
            '<div class="VAS-glje-dialog-desc"></div>' +
            '<div class="VAS-glje-dialog-sub">&mdash;</div>' +
            "</div>" +

            '<button type="button" class="VAS-glje-dialog-close" aria-label="' +
            esc(lbl("VAS_Close", "Close")) + '">' +
            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
            'stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
            '<line x1="18" y1="6" x2="6" y2="18"></line>' +
            '<line x1="6" y1="6" x2="18" y2="18"></line>' +
            "</svg>" +
            "</button>" +

            "</div>" +

            '<div class="VAS-glje-dialog-body VAS-glje-detail-body">' +
            '<div class="VAS-glje-dialog-busy">' +
            '<div class="vis-busyindicatorinnerwrap">' +
            '<i class="vis_widgetloader"></i>' +
            "</div>" +
            "</div>" +
            '<div class="VAS-glje-detail-content"></div>' +
            "</div>" +

            '<div class="VAS-glje-dialog-footer">' +

            '<button type="button" class="VAS-glje-export VAS-glje-download">' +
            esc(lbl("VAS_DownloadPDF", "Download PDF")) +
            "</button>" +

            '<div class="VAS-glje-dialog-actions">' +

            '<button type="button" ' +
            'class="VAS-glje-close-primary VAS-glje-action-post">' +
            esc(lbl("VAS_041_PostJournal", "Post journal")) +
            "</button>" +

            '<button type="button" ' +
            'class="VAS-glje-close-primary VAS-glje-detail-close">' +
            esc(lbl("VAS_Close", "Close")) +
            "</button>" +

            "</div>" +
            "</div>" +
            "</div>" +
            "</div>"
        );

        $body = $dialog.find(".VAS-glje-detail-content");
        $busy = $dialog.find(".VAS-glje-dialog-busy");
        $postButton = $dialog.find(".VAS-glje-action-post");
        $downloadButton = $dialog.find(".VAS-glje-download");
        $closeButton = $dialog.find(".VAS-glje-detail-close");

        showBusy(false);

        $postButton.on("click", function () {
            executeJournalAction("PostJournal", "post");
        });

        $downloadButton.on("click", printCurrentPopup);

        $dialog.find(
            ".VAS-glje-dialog-close, " +
            ".VAS-glje-detail-close, " +
            ".VAS-glje-dialog-scrim"
        ).on("click", close);

        /* Line prev/next (delegated - survives tbody swaps). */
        $body.on(
            "click.VASGLJDLinePager",
            ".VAS-glje-line-prev, .VAS-glje-line-next",
            function () {
                if (actionInProgress || selectedJournalId <= 0) {
                    return;
                }

                var nextPage =
                    $(this).hasClass("VAS-glje-line-prev")
                        ? linePageNo - 1
                        : linePageNo + 1;

                if (nextPage < 1 || nextPage > lineTotalPages) {
                    return;
                }

                linePageNo = nextPage;

                loadJournalDetail(selectedJournalId, true);
            }
        );

        $(document).on("keydown.VASGLJDDetail", function (event) {
            if (
                event.key === "Escape" &&
                !actionInProgress &&
                $dialog &&
                $dialog.is(":visible")
            ) {
                close();
            }
        });

        $("body").append($dialog);

        built = true;

        updateActionButtons();
    }

    /* ---- open / close --------------------------------------------------- */

    function open(journalId, opts) {
        journalId = parseInt(journalId, 10);

        if (isNaN(journalId) || journalId <= 0) {
            return;
        }

        ensureBuilt();

        currentOpts = opts || {};

        showDownload = !!currentOpts.showDownload;

        selectedJournalId = journalId;
        selectedJournalStatus = "";
        selectedJournalPosted = false;
        detailLoaded = false;
        linePageNo = 1;
        lineTotalPages = 1;

        $body.empty();

        $dialog.find(".VAS-glje-dialog-title").html("&mdash;");
        $dialog.find(".VAS-glje-dialog-desc").text("").removeAttr("title");
        $dialog.find(".VAS-glje-dialog-sub").html("&mdash;");

        updateActionButtons();

        lockBodyScroll(true);

        $dialog.css("z-index", "1000002").show();

        showBusy(true);

        loadJournalDetail(journalId);
    }

    function close() {
        if (!$dialog || actionInProgress) {
            return;
        }

        if (detailRequest && detailRequest.readyState !== 4) {
            detailRequest.abort();
        }

        selectedJournalId = 0;
        selectedJournalStatus = "";
        selectedJournalPosted = false;
        detailLoaded = false;

        updateActionButtons();

        lockBodyScroll(false);

        $dialog.hide();
    }

    /* ---- load + render -------------------------------------------------- */

    function loadJournalDetail(journalId, linePageOnly) {
        journalId = parseInt(journalId, 10);

        if (isNaN(journalId) || journalId <= 0) {
            renderDetailError(lbl("VAS_041_InvalidJournalID", "Invalid journal ID."));
            return;
        }

        if (detailRequest && detailRequest.readyState !== 4) {
            detailRequest.abort();
        }

        if (!linePageOnly) {
            detailLoaded = false;
            updateActionButtons();
            showBusy(true);
            $body.empty();
        }

        detailRequest = $.ajax({
            url: VIS.Application.contextUrl + CTRL + "GetJournalEntryDetail",
            type: "GET",
            dataType: "json",
            cache: false,
            data: {
                journalId: journalId,
                pageNo: linePageNo,
                pageSize: linePageSize
            },
            success: function (result) {
                var data = normalizeResponse(result);

                if (
                    data &&
                    !data.error &&
                    data.success !== false &&
                    data.Journal
                ) {
                    renderJournalDetail(data, linePageOnly);
                }
                else {
                    renderDetailError(
                        (data && (data.errorText || data.error || data.message)) ||
                        lbl("VAS_041_JournalDetailsNotFound", "Journal details not found.")
                    );
                }
            },
            error: function (xhr, textStatus, errorThrown) {
                if (textStatus === "abort") {
                    return;
                }

                renderDetailError(
                    getAjaxErrorMessage(
                        xhr,
                        errorThrown ||
                        lbl("VIS_Error", "Error loading journal details.")
                    )
                );
            },
            complete: function () {
                detailRequest = null;
                showBusy(false);
            }
        });
    }

    function renderJournalDetail(data, linePageOnly) {
        var journal = data.Journal || {};

        var lines =
            Array.isArray(data.Lines) ? data.Lines : [];

        linePageNo = Number(data.LinePageNo || linePageNo || 1);
        linePageSize = Number(data.LinePageSize || linePageSize || 20);
        lineTotalPages = Math.max(Number(data.LineTotalPages || 1), 1);

        var detailLineCount = Number(data.LineCount || lines.length || 0);

        var symbol = data.CurSymbol || data.ISOCode || "";
        var precision = Number(data.StdPrecision);

        var returnedJournalId = parseInt(journal.GL_Journal_ID, 10);

        if (!isNaN(returnedJournalId) && returnedJournalId > 0) {
            selectedJournalId = returnedJournalId;
        }

        selectedJournalStatus =
            String(journal.DocStatus || "").toUpperCase();

        selectedJournalPosted = isPosted(journal.Posted);

        var statusText =
            journal.StatusName ||
            journal.DocStatus ||
            selectedJournalStatus ||
            "";

        var pillClass =
            PILL_CLASS[selectedJournalStatus] || "VAS-glje-pill-draft";

        /* Line-page fast path: only swap tbody + refresh pager in place. */
        if (
            linePageOnly &&
            $body &&
            $body.find(".VAS-glje-detail-lines-wrap tbody").length
        ) {
            $body.find(".VAS-glje-detail-lines-wrap tbody").html(
                buildDetailLineRows(lines, precision)
            );

            applyLinePagerState(detailLineCount);

            detailLoaded = true;

            return;
        }

        $dialog.find(".VAS-glje-dialog-title").text(
            (journal.DocumentNo || "") +
            " · " +
            (journal.AccountingBook || lbl("VAS_041_Primary", "Primary"))
        );

        var fullDescription = String(journal.Description || "");

        var $desc = $dialog.find(".VAS-glje-dialog-desc");

        $desc.text(fullDescription);

        if (fullDescription) {
            $desc.attr("title", fullDescription);
        }
        else {
            $desc.removeAttr("title");
        }

        $dialog.find(".VAS-glje-dialog-sub").text(
            statusText + " · " + (journal.DateAcct || "")
        );

        var totalDebitAmt = formatAmount(journal.TotalDebit, precision);
        var totalCreditAmt = formatAmount(journal.TotalCredit, precision);

        var totalDebit = symbol + totalDebitAmt;
        var totalCredit = symbol + totalCreditAmt;

        var accountingBook =
            journal.AccountingBook || lbl("VAS_041_Primary", "Primary");

        var currencyText = data.ISOCode || symbol;

        var html =
            '<div class="VAS-glje-detail-summary">' +

            "<div>" +
            "<span>" + esc(lbl("VAS_041_JournalNo", "Journal No.")) + "</span>" +
            "<strong>" + esc(journal.DocumentNo) + "</strong>" +
            "</div>" +

            "<div>" +
            "<span>" + esc(lbl("VAS_Date", "Date")) + "</span>" +
            "<strong>" + esc(journal.DateAcct) + "</strong>" +
            "</div>" +

            "<div>" +
            "<span>" + esc(lbl("Status", "Status")) + "</span>" +
            "<strong>" +
            '<span class="VAS-glje-pill ' + pillClass + '">' +
            "<span></span>" +
            esc(statusText) +
            "</span>" +
            "</strong>" +
            "</div>" +

            "<div>" +
            "<span>" + esc(lbl("VAS_041_AccountingBook", "Accounting Book")) + "</span>" +
            "<strong>" + esc(accountingBook) + "</strong>" +
            "</div>" +

            "<div>" +
            "<span>" + esc(lbl("VAS_PaymentCurrency", "Currency")) + "</span>" +
            "<strong>" + esc(currencyText) + "</strong>" +
            "</div>" +

            "<div>" +
            "<span>" + esc(lbl("Description", "Description")) + "</span>" +
            "<strong>" + esc(journal.Description) + "</strong>" +
            "</div>" +

            "</div>" +

            '<div class="VAS-glje-detail-lines-wrap">' +
            '<table class="VAS-glje-detail-lines">' +
            "<thead>" +
            "<tr>" +
            "<th>" + esc(lbl("Account", "Account")) + "</th>" +
            "<th>" + esc(lbl("VAS_041_Debit", "Debit")) + "</th>" +
            "<th>" + esc(lbl("VAS_041_Credit", "Credit")) + "</th>" +
            "<th>" + esc(lbl("VAS_CostCenter", "Cost Center")) + "</th>" +
            "<th>" + esc(lbl("C_BPartner_ID", "Business Partner")) + "</th>" +
            "<th>" + esc(lbl("M_Product_ID", "Product")) + "</th>" +
            "<th>" + esc(lbl("C_Project_ID", "Project")) + "</th>" +
            "</tr>" +
            "</thead>" +
            "<tbody>";

        html += buildDetailLineRows(lines, precision);

        html +=
            "</tbody>" +
            "</table>" +
            "</div>" +

            /* Total row lives outside the scrolling table (fixed at the bottom),
               7 explicit cells keep columns aligned with the table above. */
            '<div class="VAS-glje-detail-total-wrap">' +
            '<table class="VAS-glje-detail-lines VAS-glje-detail-total-table">' +
            "<tbody><tr>" +
            "<td>" + esc(lbl("Total", "Total")) + "</td>" +
            '<td class="VAS-glje-amt" title="' + esc(totalDebit) + '">' + esc(totalDebit) + "</td>" +
            '<td class="VAS-glje-amt" title="' + esc(totalCredit) + '">' + esc(totalCredit) + "</td>" +
            "<td></td><td></td><td></td><td></td>" +
            "</tr></tbody>" +
            "</table>" +
            "</div>" +

            '<div class="VAS-glje-line-pager">' +

            '<span class="VAS-glje-page-text">' +
            esc(
                detailLineCount
                    ? formatRangeText(linePageNo, linePageSize, detailLineCount)
                    : ""
            ) +
            "</span>" +

            '<button type="button" class="VAS-glje-page-btn VAS-glje-line-prev" ' +
            (linePageNo <= 1 || lineTotalPages <= 1 ? "disabled " : "") +
            'aria-label="' + esc(lbl("VIS_Previous", "Previous")) + '">&#8249;</button>' +

            '<span class="VAS-glje-page-count">' +
            esc(
                detailLineCount
                    ? (linePageNo + " " + lbl("VIS_Of", "of") + " " + lineTotalPages)
                    : ""
            ) +
            "</span>" +

            '<button type="button" class="VAS-glje-page-btn VAS-glje-line-next" ' +
            (linePageNo >= lineTotalPages || lineTotalPages <= 1 ? "disabled " : "") +
            'aria-label="' + esc(lbl("VIS_Next", "Next")) + '">&#8250;</button>' +

            "</div>" +

            '<div class="VAS-glje-created-strip">' +
            '<span class="VAS-glje-avatar">' +
            esc(initials(journal.CreatedByName)) +
            "</span>" +
            "<div>" +
            "<span>" + esc(lbl("VAS_041_CreatedBy", "Created By")) + "</span>" +
            "<strong>" + esc(journal.CreatedByName || "-") + "</strong>" +
            (
                journal.CreatedDate
                    ? (" · " + lbl("VAS_041_Drafted", "drafted") + " " + esc(journal.CreatedDate))
                    : ""
            ) +
            "</div>" +
            "</div>";

        $body.html(html);

        detailLoaded = true;

        updateActionButtons();
    }

    function renderDetailError(message) {
        detailLoaded = false;
        linePageNo = 1;
        lineTotalPages = 1;
        selectedJournalStatus = "";
        selectedJournalPosted = false;

        $body.html(
            '<div class="VAS-glje-dialog-empty">' +
            esc(message || lbl("VIS_Error", "Error loading journal details.")) +
            "</div>"
        );

        updateActionButtons();
    }

    /* ---- action buttons (approve / post) -------------------------------- */

    function updateActionButtons() {
        var status = String(selectedJournalStatus || "").toUpperCase();

        var canPost =
            detailLoaded &&
            !selectedJournalPosted &&
            (status === "AP" || status === "CO" || status === "CL");

        if ($postButton) {
            $postButton
                .toggle(canPost)
                .prop(
                    "disabled",
                    actionInProgress || !canPost || selectedJournalId <= 0
                );
        }

        if ($downloadButton) {
            $downloadButton
                .toggle(showDownload)
                .prop(
                    "disabled",
                    actionInProgress || selectedJournalId <= 0 || !detailLoaded
                );
        }

        if ($closeButton) {
            $closeButton.prop("disabled", actionInProgress);
        }
    }

    function setActionBusy(busy, actionType) {
        showBusy(busy);

        if ($postButton) {
            $postButton.text(
                busy && actionType === "post"
                    ? lbl("VAS_041_Posting", "Posting...")
                    : lbl("VAS_041_PostJournal", "Post journal")
            );
        }

        updateActionButtons();
    }

    function executeJournalAction(actionName, actionType) {
        if (actionInProgress || selectedJournalId <= 0 || !detailLoaded) {
            return;
        }

        var status = String(selectedJournalStatus || "").toUpperCase();

        if (
            actionType === "post" &&
            status !== "AP" && status !== "CO" && status !== "CL"
        ) {
            return;
        }

        actionInProgress = true;

        setActionBusy(true, actionType);

        actionRequest = $.ajax({
            url: VIS.Application.contextUrl + CTRL + actionName,
            type: "POST",
            dataType: "json",
            cache: false,
            data: {
                journalId: selectedJournalId
            },
            success: function (result) {
                var data = normalizeResponse(result);

                if (!data || data.success === false || data.error) {
                    showProcessError(
                        (data && (data.errorText || data.error || data.message)) ||
                        lbl("VAS_041_JournalProcessFailed", "Journal process failed.")
                    );

                    return;
                }

                selectedJournalStatus =
                    String(data.docStatus || selectedJournalStatus).toUpperCase();

                selectedJournalPosted = isPosted(data.posted);

                notifyChanged();

                loadJournalDetail(selectedJournalId);
            },
            error: function (xhr, textStatus) {
                if (textStatus === "abort") {
                    return;
                }

                showProcessError(
                    getAjaxErrorMessage(
                        xhr,
                        lbl("VAS_041_JournalProcessFailed", "Journal process failed.")
                    )
                );
            },
            complete: function () {
                actionRequest = null;
                actionInProgress = false;
                setActionBusy(false, actionType);
            }
        });
    }

    function notifyChanged() {
        if (typeof currentOpts.onChanged === "function") {
            try {
                currentOpts.onChanged();
            }
            catch (error) {
                /* Host refresh failures must not break the modal. */
                if (window.console && console.error) {
                    console.error("GLJournalDetailDialog onChanged failed:", error);
                }
            }
        }
    }

    /* ---- print / PDF ---------------------------------------------------- */

    function printCurrentPopup() {
        if (
            !$dialog ||
            !$dialog.is(":visible") ||
            selectedJournalId <= 0 ||
            !detailLoaded
        ) {
            showProcessError(
                lbl("VAS_041_DetailsNotLoaded", "Journal details are not loaded.")
            );

            return;
        }

        actionInProgress = true;

        showBusy(true);

        updateActionButtons();

        $.ajax({
            url: VIS.Application.contextUrl + CTRL + "GetJournalPrintInfo",
            type: "GET",
            dataType: "json",
            cache: false,
            data: {
                journalId: selectedJournalId
            },
            success: function (rawInfo) {
                var info = normalizeResponse(rawInfo);

                var processId =
                    Number(info && (info.AD_Process_ID || info.ad_Process_ID || info.adProcessId)) || 0;

                var tableId =
                    Number(info && (info.AD_Table_ID || info.ad_Table_ID || info.adTableId)) || 0;

                if (processId <= 0 || tableId <= 0) {
                    finishPdfDownload();

                    showProcessError(
                        lbl("VAS_041_PrintProcessNotFound", "Print process is not configured for GL Journal.")
                    );

                    return;
                }

                generateJournalPDF(processId, tableId);
            },
            error: function (xhr) {
                finishPdfDownload();

                showProcessError(
                    getAjaxErrorMessage(
                        xhr,
                        lbl("VAS_041_PrintProcessNotFound", "Print process is not configured for GL Journal.")
                    )
                );
            }
        });
    }

    function generateJournalPDF(processId, tableId) {
        $.ajax({
            url: VIS.Application.contextUrl + "JsonData/GeneratePrint/",
            dataType: "json",
            data: {
                AD_Process_ID: processId,
                Name: "Print",
                AD_Table_ID: tableId,
                Record_ID: selectedJournalId,
                WindowNo: (currentOpts && currentOpts.windowNo) || 0,
                filetype: "P",
                actionOrigin: "W",
                originName: lbl("VAS_041_GLJournal", "GL Journal")
            },
            success: function (raw) {
                var res = normalizeResponse(raw);

                var file =
                    res &&
                    (res.ReportFilePath || res.FilePath || res.FileName || res.fileName || res.path);

                if (!file) {
                    showProcessError(
                        (res && res.ReportProcessInfo && res.ReportProcessInfo.Summary) ||
                        (res && res.ErrorText) ||
                        lbl("VAS_041_PrintFailed", "Could not generate the PDF.")
                    );

                    return;
                }

                window.open(VIS.Application.contextUrl + file, "_blank");
            },
            error: function (xhr) {
                showProcessError(
                    getAjaxErrorMessage(
                        xhr,
                        lbl("VAS_041_PrintFailed", "Could not generate the PDF.")
                    )
                );
            },
            complete: finishPdfDownload
        });
    }

    function finishPdfDownload() {
        actionInProgress = false;
        showBusy(false);
        updateActionButtons();
    }

    /* ---- public API ----------------------------------------------------- */

    VAS.GLJournalDetailDialog = {
        open: open,
        close: close,
        isBusy: function () {
            return actionInProgress;
        },
        isOpen: function () {
            return !!($dialog && $dialog.is(":visible"));
        }
    };

})(VAS, jQuery);
