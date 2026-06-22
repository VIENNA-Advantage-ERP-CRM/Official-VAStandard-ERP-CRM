/**
 * VAS_047_TodayCashInCashJournalWidget
 * Purpose - Shows today's total cash-in amount from the Cash Journal
 *           as a KPI tile with a delta badge vs the 7-day rolling average
 *           and a receipt count.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+----------------------------------
 *  1  | Cash in                              | VAS_047_CashInTitle
 *  2  | Today                                | VAS_047_PeriodToday
 *  3  | vs 7-day avg                         | VAS_047_Vs7DayAvg
 *  4  | receipts                             | VAS_047_Receipts
 *  5  | Total cash received in cash journal  | VAS_047_Description
 *     | today                                |
 *  6  | No data available                    | VAS_047_NoData
 *  7  | Could not load data                  | VAS_047_LoadError
 *  8  | Session Expired                      | VAS_047_SessionExpired
 *  9  | Today's Cash Receipts                 | VAS_047_DialogTitle
 * 10  | Completed cash journal receipts       | VAS_047_DialogSubtitle
 *     | recorded today                        |
 * 11  | Document No.                          | VAS_047_DocumentNo
 * 12  | Date                                  | VAS_047_Date
 * 13  | Description                           | VAS_047_DialogDescription
 * 14  | Cash Type                             | VAS_047_CashType
 * 15  | Cash Book                             | VAS_047_CashBook
 * 16  | Amount                                | VAS_047_Amount
 * 17  | Close                                 | VAS_047_Close
 * 18  | No cash receipts found today          | VAS_047_NoReceipts
 * 19  | receipt                               | VAS_047_Receipt
 * 20  | Showing                               | VAS_047_Showing
 * 21  | of                                    | VAS_047_Of
 * 22  | total                                 | VAS_047_Total
 * 23  | Previous                              | VAS_047_Previous
 * 24  | Next                                  | VAS_047_Next
 * 25  | Charge                                | VAS_047_Charge
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    /**
     * VAS_047_TodayCashInCashJournalWidget
     * KPI widget: Today Cash In — Cash Journal
     */
    VAS.VAS_047_TodayCashInCashJournalWidget = function () {

        /* ── Private references ─────────────────────────────────────── */
        var $self = this;
        var $root = null;
        var $dialog = null;
        var $dialogTbody = null;
        var $dialogBusy = null;
        var $pagerHelper = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;
        var $previousFocus = null;
        var rowsRequest = null;
        var rowsLoading = false;
        var pageNo = 1;
        var pageSize = 6;
        var totalPages = 0;
        var totalRecords = 0;
        var totalAmount = 0;
        var dialogPrecision = 2;
        var dialogCurrencyLabel = '';
        var eventNamespace = '';

        /* ── Label helper ───────────────────────────────────────────── */
        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        function getPrecision(precision) {
            var stdPrecision = Number(precision);

            return isNaN(stdPrecision) || stdPrecision < 0 ? 2 : stdPrecision;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function formatAmount(value, precision) {
            var numericValue = Number(value || 0);
            var stdPrecision = getPrecision(precision);

            return numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
        }

        function formatDate(value) {
            var match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value || '');

            if (!match) {
                return value || '';
            }

            return new Date(
                Number(match[1]),
                Number(match[2]) - 1,
                Number(match[3])
            ).toLocaleDateString(window.navigator.language, {
                year: 'numeric',
                month: 'short',
                day: '2-digit'
            });
        }

        /* ── Amount formatter ───────────────────────────────────────── */
        function getCurrencyLabel(currencySymbol, currencyISO) {
            return currencySymbol || currencyISO || '';
        }

        function getAmountParts(value, precision) {
            var numericValue = Number(value || 0);
            var stdPrecision = getPrecision(precision);
            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            var decimalMatch = amount.match(/([.,]\d+)$/);

            return {
                sign: numericValue < 0 ? '-' : '',
                main: decimalMatch ? amount.substring(0, amount.length - decimalMatch[1].length) : amount,
                decimal: decimalMatch ? decimalMatch[1] : ''
            };
        }

        function renderCurrencyAmount($target, value, currencySymbol, currencyISO, precision) {
            var parts = getAmountParts(value, precision);
            var prefix = parts.sign + getCurrencyLabel(currencySymbol, currencyISO);

            $target.empty()
                .append($('<span>', {
                    'class': 'VAS-cash-amount-prefix',
                    'text': prefix
                }))
                .append($('<span>', {
                    'class': 'VAS-cash-amount-main',
                    'text': parts.main
                }))
                .append($('<span>', {
                    'class': 'VAS-cash-amount-decimal',
                    'text': parts.decimal
                }));
        }

        /* ── Loading overlay ────────────────────────────────────────── */
        function showBusy(show) {
            var $b = $root.find('#VAS-047-cj-busy-' + $self.AD_UserHomeWidgetID);

            if (show) {
                $b.addClass('is-visible');
            } else {
                $b.removeClass('is-visible');
            }
        }

        /* ── State overlay ──────────────────────────────────────────── */
        function showState(show, message) {
            var uid = $self.AD_UserHomeWidgetID;
            var $s = $root.find('#VAS-047-cj-state-' + $self.AD_UserHomeWidgetID);

            if (show) {
                $s.text(message || '').addClass('is-visible');
                $root.find('#VAS-047-cj-value-' + uid).text('');
                $root.find('.VAS-047-today-cash-in-cash-journal-body').hide();
                $root.find('.VAS-047-today-cash-in-cash-journal-footer').hide();
            } else {
                $s.removeClass('is-visible').text('');
                $root.find('.VAS-047-today-cash-in-cash-journal-body').show();
                $root.find('.VAS-047-today-cash-in-cash-journal-footer').show();
            }
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) {
                return;
            }

            $dialogBusy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function renderDialogMessage(message) {
            if (!$dialogTbody) {
                return;
            }

            $dialogTbody.html(
                '<tr><td class="VAS-047-cash-in-dialog-empty" colspan="7">' +
                escapeHtml(message) +
                '</td></tr>'
            );
        }

        function renderDialogRows(rows) {
            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                renderDialogMessage(
                    lbl(
                        'VAS_047_NoReceipts',
                        'No cash receipts found today'
                    )
                );
                return;
            }

            $.each(rows, function (index, row) {
                var documentNo = row.documentNo || '-';
                var dateText = formatDate(row.statementDate);
                var description = row.description || '-';
                var cashType = row.cashTypeName || row.cashTypeValue || '-';
                var charge = row.chargeName || '-';
                var cashBook = row.cashBookName || '-';
                var amountText = formatAmount(
                    row.amount,
                    row.stdPrecision
                );
                var amountWithCurrency = dialogCurrencyLabel
                    ? dialogCurrencyLabel + ' ' + amountText
                    : amountText;

                $dialogTbody.append(
                    '<tr>' +
                    '<td class="VAS-047-cash-in-td-document" style="text-align:left" title="' + escapeHtml(documentNo) + '"><span class="VAS-047-cash-in-truncate">' + escapeHtml(documentNo) + '</span></td>' +
                    '<td class="VAS-047-cash-in-td-date" style="text-align:left" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</td>' +
                    '<td class="VAS-047-cash-in-td-type" style="text-align:left" title="' + escapeHtml(cashType) + '"><span class="VAS-047-cash-in-type-pill">' + escapeHtml(cashType) + '</span></td>' +
                    '<td class="VAS-047-cash-in-td-charge" style="text-align:left" title="' + escapeHtml(charge) + '"><span class="VAS-047-cash-in-truncate">' + escapeHtml(charge) + '</span></td>' +
                    '<td class="VAS-047-cash-in-td-book" style="text-align:left" title="' + escapeHtml(cashBook) + '"><span class="VAS-047-cash-in-truncate">' + escapeHtml(cashBook) + '</span></td>' +
                    '<td class="VAS-047-cash-in-td-amount" style="text-align:left" title="' + escapeHtml(amountWithCurrency) + '">' + escapeHtml(amountWithCurrency) + '</td>' +
                    '<td class="VAS-047-cash-in-td-description" style="text-align:left" title="' + escapeHtml(description) + '"><span class="VAS-047-cash-in-truncate">' + escapeHtml(description) + '</span></td>' +
                    '</tr>'
                );
            });
        }

        function updateDialogPager() {
            var from = totalRecords > 0
                ? ((pageNo - 1) * pageSize) + 1
                : 0;
            var to = Math.min(pageNo * pageSize, totalRecords);

            if ($pagerHelper) {
                if (totalRecords > 0) {
                    $pagerHelper.text(
                        lbl('VAS_047_Showing', 'Showing') + ' ' +
                        from + '\u2013' + to + ' ' +
                        lbl('VAS_047_Of', 'of') + ' ' +
                        totalRecords + ' ' +
                        lbl(
                            totalRecords === 1
                                ? 'VAS_047_Receipt'
                                : 'VAS_047_Receipts',
                            totalRecords === 1
                                ? 'receipt'
                                : 'receipts'
                        ) +
                        ' \u00B7 ' +
                        lbl('VAS_047_Total', 'total') + ' ' +
                        (dialogCurrencyLabel ? dialogCurrencyLabel + ' ' : '') +
                        formatAmount(totalAmount, dialogPrecision)
                    );
                }
                else {
                    $pagerHelper.text('');
                }
            }

            if ($pagerText) {
                $pagerText.text(
                    totalPages > 0
                        ? pageNo + ' ' + lbl('VAS_047_Of', 'of') + ' ' + totalPages
                        : ''
                );
            }

            if ($pagerPrev) {
                $pagerPrev.prop(
                    'disabled',
                    rowsLoading || pageNo <= 1
                );
            }

            if ($pagerNext) {
                $pagerNext.prop(
                    'disabled',
                    rowsLoading || totalPages <= 1 || pageNo >= totalPages
                );
            }
        }

        function loadDialogRows() {
            if (!$dialogTbody || rowsLoading) {
                return;
            }

            rowsLoading = true;
            showDialogBusy(true);
            updateDialogPager();

            rowsRequest = $.ajax({
                url: VIS.Application.contextUrl +
                    'VAS_047_TodayCashInCashJournalWidget/GetTodayCashInRows',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: {
                    pageNo: pageNo,
                    pageSize: pageSize
                },
                success: function (response) {
                    var data = response;

                    if (typeof data === 'string') {
                        try {
                            data = JSON.parse(data);
                        }
                        catch (ignore) {
                            data = null;
                        }
                    }

                    if (!data || typeof data !== 'object') {
                        renderDialogMessage(
                            lbl('VAS_047_LoadError', 'Could not load data')
                        );
                        return;
                    }

                    if (data.error === 'VAS_047_SessionExpired') {
                        renderDialogMessage(
                            lbl('VAS_047_SessionExpired', 'Session Expired')
                        );
                        return;
                    }

                    if (data.success === false || data.error) {
                        renderDialogMessage(
                            data.error ||
                            lbl('VAS_047_LoadError', 'Could not load data')
                        );
                        return;
                    }

                    pageNo = Number(data.pageNo || pageNo);
                    totalPages = Number(data.totalPages || 0);
                    totalRecords = Number(data.totalRecords || 0);
                    totalAmount = Number(data.totalAmount || 0);
                    dialogPrecision = getPrecision(data.stdPrecision);

                    renderDialogRows(data.rows || []);
                },
                error: function () {
                    renderDialogMessage(
                        lbl('VAS_047_LoadError', 'Could not load data')
                    );
                },
                complete: function () {
                    rowsLoading = false;
                    rowsRequest = null;
                    showDialogBusy(false);
                    updateDialogPager();
                }
            });
        }

        function openDialog() {
            if (!$dialog) {
                return;
            }

            $previousFocus = $(document.activeElement);
            pageNo = 1;
            totalPages = 0;
            totalRecords = 0;
            totalAmount = 0;
            $dialog.css('display', 'flex');
            $('body').addClass('VAS-047-cash-in-body-lock');
            $dialog.find('.VAS-047-cash-in-dialog-close').trigger('focus');
            loadDialogRows();
        }

        function closeDialog() {
            if (!$dialog) {
                return;
            }

            if (rowsRequest && rowsRequest.readyState !== 4) {
                rowsRequest.abort();
            }

            $dialog.hide();
            $('body').removeClass('VAS-047-cash-in-body-lock');

            if ($previousFocus && $previousFocus.length) {
                $previousFocus.trigger('focus');
            }

            $previousFocus = null;
        }

        function createDialog() {
            var uid = $self.AD_UserHomeWidgetID;
            var titleId = 'VAS-047-cash-in-dialog-title-' + uid;

            $dialog = $(
                '<div class="VAS-047-cash-in-dialog" style="display:none" role="dialog" aria-modal="true" aria-labelledby="' + titleId + '">' +
                '<div class="VAS-047-cash-in-dialog-scrim"></div>' +
                '<div class="VAS-047-cash-in-dialog-card">' +
                '<div class="VAS-047-cash-in-dialog-header">' +
                '<div class="VAS-047-cash-in-dialog-icon" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 2v20l2-2 2 2 2-2 2 2 2-2 2 2 2-2 2 2V2l-2 2-2-2-2 2-2-2-2 2-2-2-2 2-2-2Z"/><path d="M8 9h8"/><path d="M8 13h6"/></svg>' +
                '</div>' +
                '<div class="VAS-047-cash-in-dialog-title-group">' +
                '<div class="VAS-047-cash-in-dialog-title" id="' + titleId + '">' + escapeHtml(lbl('VAS_047_DialogTitle', "Today's Cash Receipts")) + '</div>' +
                '<div class="VAS-047-cash-in-dialog-subtitle">' + escapeHtml(lbl('VAS_047_DialogSubtitle', 'Completed cash journal receipts recorded today')) + '</div>' +
                '</div>' +
                '<button type="button" class="VAS-047-cash-in-dialog-close" aria-label="' + escapeHtml(lbl('VAS_047_Close', 'Close')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>' +
                '</div>' +
                '<div class="VAS-047-cash-in-dialog-body">' +
                '<div class="VAS-047-cash-in-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="VAS-047-cash-in-dialog-table">' +
                '<thead><tr>' +
                '<th class="VAS-047-cash-in-th-document" style="text-align:left" title="' + escapeHtml(lbl('VAS_047_DocumentNo', 'Document No.')) + '">' + escapeHtml(lbl('VAS_047_DocumentNo', 'Document No.')) + '</th>' +
                '<th class="VAS-047-cash-in-th-date" style="text-align:left" title="' + escapeHtml(lbl('VAS_047_Date', 'Date')) + '">' + escapeHtml(lbl('VAS_047_Date', 'Date')) + '</th>' +
                '<th class="VAS-047-cash-in-th-type" style="text-align:left" title="' + escapeHtml(lbl('VAS_047_CashType', 'Cash Type')) + '">' + escapeHtml(lbl('VAS_047_CashType', 'Cash Type')) + '</th>' +
                '<th class="VAS-047-cash-in-th-charge" style="text-align:left" title="' + escapeHtml(lbl('VAS_047_Charge', 'Charge')) + '">' + escapeHtml(lbl('VAS_047_Charge', 'Charge')) + '</th>' +
                '<th class="VAS-047-cash-in-th-book" style="text-align:left" title="' + escapeHtml(lbl('VAS_047_CashBook', 'Cash Book')) + '">' + escapeHtml(lbl('VAS_047_CashBook', 'Cash Book')) + '</th>' +
                '<th class="VAS-047-cash-in-th-amount" style="text-align:left" title="' + escapeHtml(lbl('VAS_047_Amount', 'Amount')) + '">' + escapeHtml(lbl('VAS_047_Amount', 'Amount')) + '</th>' +
                '<th class="VAS-047-cash-in-th-description" style="text-align:left" title="' + escapeHtml(lbl('VAS_047_DialogDescription', 'Description')) + '">' + escapeHtml(lbl('VAS_047_DialogDescription', 'Description')) + '</th>' +
                '</tr></thead>' +
                '<tbody class="VAS-047-cash-in-dialog-tbody"></tbody>' +
                '</table>' +
                '</div>' +
                '<div class="VAS-047-cash-in-dialog-footer">' +
                '<span class="VAS-047-cash-in-pager-helper"></span>' +
                '<div class="VAS-047-cash-in-dialog-actions">' +
                '<div class="VAS-047-cash-in-pager">' +
                '<button type="button" class="VAS-047-cash-in-pager-btn VAS-047-cash-in-pager-prev" aria-label="' + escapeHtml(lbl('VAS_047_Previous', 'Previous')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                '<span class="VAS-047-cash-in-pager-text"></span>' +
                '<button type="button" class="VAS-047-cash-in-pager-btn VAS-047-cash-in-pager-next" aria-label="' + escapeHtml(lbl('VAS_047_Next', 'Next')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                '</div>' +
                '<button type="button" class="VAS-047-cash-in-dialog-close-action">' + escapeHtml(lbl('VAS_047_Close', 'Close')) + '</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $dialogTbody = $dialog.find('.VAS-047-cash-in-dialog-tbody');
            $dialogBusy = $dialog.find('.VAS-047-cash-in-dialog-busy');
            $pagerHelper = $dialog.find('.VAS-047-cash-in-pager-helper');
            $pagerPrev = $dialog.find('.VAS-047-cash-in-pager-prev');
            $pagerNext = $dialog.find('.VAS-047-cash-in-pager-next');
            $pagerText = $dialog.find('.VAS-047-cash-in-pager-text');
            $dialogBusy[0].style.visibility = 'hidden';

            $dialog.find('.VAS-047-cash-in-dialog-close, .VAS-047-cash-in-dialog-close-action').on('click', closeDialog);
            $dialog.find('.VAS-047-cash-in-dialog-scrim').on('click', closeDialog);

            $pagerPrev.on('click', function () {
                if (rowsLoading || pageNo <= 1) {
                    return;
                }

                pageNo--;
                loadDialogRows();
            });

            $pagerNext.on('click', function () {
                if (rowsLoading || pageNo >= totalPages) {
                    return;
                }

                pageNo++;
                loadDialogRows();
            });

            eventNamespace = '.VAS047CashIn' + String(uid).replace(/[^A-Za-z0-9]/g, '');
            $(document).on('keydown' + eventNamespace, function (event) {
                if (event.key === 'Escape' && $dialog && $dialog.is(':visible')) {
                    closeDialog();
                }
            });

            $('body').append($dialog);
        }

        /* ── Build skeleton DOM ─────────────────────────────────────── */
        function buildDOM() {
            var svgUp = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                + 'stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" '
                + 'aria-hidden="true">'
                + '<polyline points="18 15 12 9 6 15"></polyline></svg>';

            var svgCash = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                + 'stroke-width="2" stroke-linecap="round" stroke-linejoin="round" '
                + 'aria-hidden="true">'
                + '<rect x="2" y="7" width="20" height="14" rx="2" ry="2"></rect>'
                + '<path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"></path>'
                + '</svg>';

            var uid = $self.AD_UserHomeWidgetID;

            var html = ''
                + '<div class="VAS-047-today-cash-in-cash-journal-card">'

                + '  <div class="VAS-047-today-cash-in-cash-journal-header">'
                + '    <div class="VAS-047-today-cash-in-cash-journal-title-wrap">'
                + '      <span class="VAS-047-today-cash-in-cash-journal-title"'
                + '            id="VAS-047-cj-title-' + uid + '"></span>'
                + '    </div>'
                + '    <div class="VAS-047-today-cash-in-cash-journal-header-tools">'
                + '      <span class="VAS-047-today-cash-in-cash-journal-period"'
                + '            id="VAS-047-cj-period-' + uid + '"></span>'
                + '      <span class="VAS-glje-zoom" aria-hidden="true">'
                + '        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18l6-6-6-6"></path></svg>'
                + '      </span>'
                + '    </div>'
                + '  </div>'

                + '  <div class="VAS-047-today-cash-in-cash-journal-body">'
                + '    <span class="VAS-047-today-cash-in-cash-journal-value"'
                + '          id="VAS-047-cj-value-' + uid + '"></span>'
                + '  </div>'

                + '  <div class="VAS-047-today-cash-in-cash-journal-footer">'
                + '    <span class="VAS-047-today-cash-in-cash-journal-badge"'
                + '          id="VAS-047-cj-badge-' + uid + '">'
                + '      <span id="VAS-047-cj-badge-icon-' + uid + '">' + svgUp + '</span>'
                + '      <span id="VAS-047-cj-badge-pct-' + uid + '"></span>'
                + '    </span>'
                + '    <span class="VAS-047-today-cash-in-cash-journal-desc"'
                + '          id="VAS-047-cj-desc-' + uid + '"></span>'
                + '  </div>'

                + '  <div class="VAS-047-today-cash-in-cash-journal-busy vis-busyindicatorouterwrap"'
                + '       id="VAS-047-cj-busy-' + uid + '">'
                + '    <div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '  </div>'

                + '  <div class="VAS-047-today-cash-in-cash-journal-state"'
                + '       id="VAS-047-cj-state-' + uid + '"></div>'

                + '  <button type="button" class="VAS-047-today-cash-in-cash-journal-action" aria-label="' + escapeHtml(lbl('VAS_047_DialogTitle', "Today's Cash Receipts")) + '"></button>'

                + '</div>';

            $root.html(html);

            $root.find('#VAS-047-cj-title-' + uid)
                .text(lbl('VAS_047_CashInTitle', 'Cash in'));

            $root.find('#VAS-047-cj-period-' + uid)
                .text(lbl('VAS_047_PeriodToday', 'Today'));

        }

        function bindWidgetInteraction() {
            var selector =
                '.VAS-047-today-cash-in-cash-journal-action';

            $root.on('click', selector, function () {
                openDialog();
            });
        }

        /* ── Render data into DOM ───────────────────────────────────── */
        function renderData(data) {
            var uid = $self.AD_UserHomeWidgetID;
            showState(false, '');
            var mainMetric = Number(data.mainMetric || 0);
            var avgDailyAmount = Number(data.avgDailyAmount || 0);
            var deltaRaw = Number(data.deltaPercent || 0);
            var receiptCount = data.receiptCount || data.recordCount || 0;
            dialogCurrencyLabel = data.currencySymbol || data.currencyISO || '';

            if (!data.deltaPercent && avgDailyAmount > 0) {
                deltaRaw = Math.round(((mainMetric - avgDailyAmount) / avgDailyAmount) * 100);
            }

            renderCurrencyAmount($root.find('#VAS-047-cj-value-' + uid), mainMetric, data.currencySymbol, data.currencyISO, data.stdPrecision);

            var isPositive = (deltaRaw >= 0);
            var absPct = Math.abs(deltaRaw);
            var signPrefix = isPositive ? '+' : '-';
            var deltaText = signPrefix + absPct + '%';

            var $badge = $root.find('#VAS-047-cj-badge-' + uid);
            $badge.toggleClass('is-down', !isPositive);

            $root.find('#VAS-047-cj-badge-pct-' + uid).text(deltaText);

            var svgUp = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                + 'stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" '
                + 'aria-hidden="true"><polyline points="18 15 12 9 6 15"></polyline></svg>';

            var svgDown = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" '
                + 'stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" '
                + 'aria-hidden="true"><polyline points="6 9 12 15 18 9"></polyline></svg>';

            $root.find('#VAS-047-cj-badge-icon-' + uid).html(isPositive ? svgUp : svgDown);

            var vs7day = lbl('VAS_047_Vs7DayAvg', 'vs 7-day avg');
            var rcptLbl = lbl('VAS_047_Receipts', 'receipts');
            var descText = vs7day + ' \u00B7 ' + receiptCount + ' ' + rcptLbl;

            $root.find('#VAS-047-cj-desc-' + uid)
                .text(descText)
                .attr('title', descText);
        }

        /* ── Load data from controller ──────────────────────────────── */
        function loadData() {
            showBusy(true);
            showState(false, '');

            var url = VIS.Application.contextUrl
                + 'VAS_047_TodayCashInCashJournalWidget/GetTodayCashInData';

            $.ajax({
                url: url,
                type: 'GET',
                dataType: 'json',
                success: function (response) {
                    if (!response || typeof response !== 'object') {
                        showState(true, lbl('VAS_047_LoadError', 'Could not load data'));
                        return;
                    }

                    if (response.error === 'VAS_047_SessionExpired') {
                        showState(true, lbl('VAS_047_SessionExpired', 'Session Expired'));
                        return;
                    }

                    if (!response.success) {
                        var errMsg = response.error || lbl('VAS_047_LoadError', 'Could not load data');
                        showState(true, errMsg);
                        return;
                    }

                    if (!response.hasData) {
                        showState(true, lbl('VAS_047_NoData', 'No data available'));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    showState(true, lbl('VAS_047_LoadError', 'Could not load data'));
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        /* ── VIS widget lifecycle ───────────────────────────────────── */

        this.initalize = function ($el) {
            $self = this;

            if (!$self.AD_UserHomeWidgetID) {
                $self.AD_UserHomeWidgetID = $self.widgetInfo && $self.widgetInfo.AD_UserHomeWidgetID
                    ? $self.widgetInfo.AD_UserHomeWidgetID
                    : ($self.windowNo || new Date().getTime());
            }

            $root = $el ? $($el) : $('<div></div>');

            $root.addClass('VAS-047-today-cash-in-cash-journal-root');

            buildDOM();
            createDialog();
            bindWidgetInteraction();
            loadData();
        };

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (rowsRequest && rowsRequest.readyState !== 4) {
                rowsRequest.abort();
            }

            if (eventNamespace) {
                $(document).off(eventNamespace);
            }

            $('body').removeClass('VAS-047-cash-in-body-lock');

            if ($dialog) {
                $dialog.remove();
            }

            if ($root) {
                $root.off();
                $root.empty();
                $root = null;
            }

            rowsRequest = null;
            $dialog = null;
            $dialogTbody = null;
            $dialogBusy = null;
            $pagerHelper = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;
        };
    };

    VAS.VAS_047_TodayCashInCashJournalWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;

        if (frame && frame.widgetInfo) {
            this.widgetInfo = frame.widgetInfo;
            this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        }

        this.initalize();

        if (this.frame && this.frame.getContentGrid) {
            this.frame.getContentGrid().append(this.getRoot());
        }
    };

    VAS.VAS_047_TodayCashInCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_047_TodayCashInCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
