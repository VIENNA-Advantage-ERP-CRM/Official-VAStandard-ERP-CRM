/**
 * Today Cash Out — Cash Journal
 * Purpose - Shows today's cash disbursement amount from negative Cash Journal lines.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Cash out                             | VAS_048_CashOut
 *  2  | Today                                | VAS_048_Today
 *  3  | Loading                              | VAS_048_Loading
 *  4  | No data                              | VAS_048_NoData
 *  5  | Unable to load cash out              | VAS_048_LoadError
 *  6  | vs 7-day avg                         | VAS_048_VsSevenDayAvg
 *  7  | disbursements                        | VAS_048_Disbursements
 *  8  | Session Expired                      | VAS_048_SessionExpired
 *  9  | Today's Cash Disbursements            | VAS_048_DialogTitle
 * 10  | Completed cash journal disbursements  | VAS_048_DialogSubtitle
 *     | recorded today                        |
 * 11  | Document No.                          | VAS_048_DocumentNo
 * 12  | Date                                  | VAS_048_Date
 * 13  | Cash Type                             | VAS_048_CashType
 * 14  | Charge                                | VAS_048_Charge
 * 15  | Cash Book                             | VAS_048_CashBook
 * 16  | Amount                                | VAS_048_Amount
 * 17  | Description                           | VAS_048_DialogDescription
 * 18  | Close                                 | VAS_048_Close
 * 19  | No cash disbursements found today     | VAS_048_NoDisbursements
 * 20  | disbursement                          | VAS_048_Disbursement
 * 21  | Showing                               | VAS_048_Showing
 * 22  | of                                    | VAS_048_Of
 * 23  | total                                 | VAS_048_Total
 * 24  | Previous                              | VAS_048_Previous
 * 25  | Next                                  | VAS_048_Next
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_048_TodayCashOutCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;
        var rowsRequest = null;
        var $dialog = null;
        var $dialogTbody = null;
        var $dialogBusy = null;
        var $pagerHelper = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;
        var pageNo = 1;
        var pageSize = 6;
        var totalPages = 0;
        var totalRecords = 0;
        var totalAmount = 0;
        var dialogPrecision = 2;
        var dialogCurrencyLabel = '';
        var rowsLoading = false;
        var eventNamespace = '';

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== key && text !== '[' + key + ']' ? text : fallback;
        }

        function getPrecision(precision) {
            var stdPrecision = Number(precision);

            return isNaN(stdPrecision) || stdPrecision < 0 ? 2 : stdPrecision;
        }

        function getCurrencyLabel(currencySymbol, currencyISO) {
            return currencySymbol || currencyISO || '';
        }

        function getAmountParts(value, currencySymbol, currencyISO, precision) {
            var numericValue = Number(value || 0);
            var stdPrecision = getPrecision(precision);
            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            var decimalMatch = amount.match(/([.,]\d+)$/);

            return {
                prefix: (numericValue < 0 ? '-' : '') + getCurrencyLabel(currencySymbol, currencyISO),
                main: decimalMatch ? amount.substring(0, amount.length - decimalMatch[1].length) : amount,
                decimal: decimalMatch ? decimalMatch[1] : ''
            };
        }

        function renderCurrencyAmount($target, value, currencySymbol, currencyISO, precision) {
            var parts = getAmountParts(value, currencySymbol, currencyISO, precision);

            $target.empty()
                .append($('<span>', { 'class': 'VAS-cash-amount-prefix', 'text': parts.prefix }))
                .append($('<span>', { 'class': 'VAS-cash-amount-main', 'text': parts.main }))
                .append($('<span>', { 'class': 'VAS-cash-amount-decimal', 'text': parts.decimal }));
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljtm-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.addClass('is-visible'); } else { $b.removeClass('is-visible'); }
        }

        function safeNumber(value) {
            var numberValue = Number(value || 0);
            return isNaN(numberValue) ? 0 : numberValue;
        }

        function formatPercent(value) {
            var numberValue = safeNumber(value);
            var sign = numberValue > 0 ? '+' : '';
            return sign + numberValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: 0,
                maximumFractionDigits: 0
            }) + '%';
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function formatDialogAmount(value, precision) {
            return safeNumber(value).toLocaleString(window.navigator.language, {
                minimumFractionDigits: getPrecision(precision),
                maximumFractionDigits: getPrecision(precision)
            });
        }

        function formatDialogDate(value) {
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

        function showDialogBusy(show) {
            if ($dialogBusy && $dialogBusy[0]) {
                $dialogBusy[0].style.visibility = show ? 'visible' : 'hidden';
            }
        }

        function renderDialogMessage(message) {
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
                    lbl('VAS_048_NoDisbursements', 'No cash disbursements found today')
                );
                return;
            }

            $.each(rows, function (index, row) {
                var documentNo = row.documentNo || '-';
                var dateText = formatDialogDate(row.statementDate);
                var cashType = row.cashTypeName || row.cashTypeValue || '-';
                var charge = row.chargeName || '-';
                var cashBook = row.cashBookName || '-';
                var description = row.description || '-';
                var amountText = formatDialogAmount(row.amount, row.stdPrecision);
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
            var from = totalRecords > 0 ? ((pageNo - 1) * pageSize) + 1 : 0;
            var to = Math.min(pageNo * pageSize, totalRecords);

            if ($pagerHelper) {
                $pagerHelper.text(totalRecords > 0
                    ? lbl('VAS_048_Showing', 'Showing') + ' ' + from + '\u2013' + to + ' ' +
                        lbl('VAS_048_Of', 'of') + ' ' + totalRecords + ' ' +
                        lbl(totalRecords === 1 ? 'VAS_048_Disbursement' : 'VAS_048_Disbursements', totalRecords === 1 ? 'disbursement' : 'disbursements') +
                        ' \u00B7 ' + lbl('VAS_048_Total', 'total') + ' ' +
                        (dialogCurrencyLabel ? dialogCurrencyLabel + ' ' : '') +
                        formatDialogAmount(totalAmount, dialogPrecision)
                    : '');
            }

            if ($pagerText) {
                $pagerText.text(totalPages > 0
                    ? pageNo + ' ' + lbl('VAS_048_Of', 'of') + ' ' + totalPages
                    : '');
            }

            $pagerPrev.prop('disabled', rowsLoading || pageNo <= 1);
            $pagerNext.prop('disabled', rowsLoading || totalPages <= 1 || pageNo >= totalPages);
        }

        function loadDialogRows() {
            if (rowsLoading || isDisposed) {
                return;
            }

            rowsLoading = true;
            showDialogBusy(true);
            updateDialogPager();

            rowsRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_048_TodayCashOutCashJournal/GetTodayCashOutRows',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: { pageNo: pageNo, pageSize: pageSize },
                success: function (data) {
                    if (!data || typeof data !== 'object') {
                        renderDialogMessage(lbl('VAS_048_LoadError', 'Unable to load cash out'));
                        return;
                    }

                    if (data.error === 'VAS_048_SessionExpired') {
                        renderDialogMessage(lbl('VAS_048_SessionExpired', 'Session Expired'));
                        return;
                    }

                    if (data.success === false || data.error) {
                        renderDialogMessage(data.error || lbl('VAS_048_LoadError', 'Unable to load cash out'));
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
                    renderDialogMessage(lbl('VAS_048_LoadError', 'Unable to load cash out'));
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
            if (rowsRequest && rowsRequest.readyState !== 4) {
                rowsRequest.abort();
            }

            $dialog.hide();
            $('body').removeClass('VAS-047-cash-in-body-lock');
        }

        function createDialog() {
            var widgetId = $self.AD_UserHomeWidgetID;
            var titleId = 'VAS-048-cash-out-dialog-title-' + widgetId;

            $dialog = $(
                '<div class="VAS-047-cash-in-dialog VAS-048-cash-out-dialog" style="display:none" role="dialog" aria-modal="true" aria-labelledby="' + titleId + '">' +
                '<div class="VAS-047-cash-in-dialog-scrim"></div>' +
                '<div class="VAS-047-cash-in-dialog-card">' +
                '<div class="VAS-047-cash-in-dialog-header">' +
                '<div class="VAS-047-cash-in-dialog-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 2v20l2-2 2 2 2-2 2 2 2-2 2 2 2-2 2 2V2l-2 2-2-2-2 2-2-2-2 2-2-2-2 2-2-2Z"/><path d="M8 9h8"/><path d="M8 13h6"/></svg></div>' +
                '<div class="VAS-047-cash-in-dialog-title-group"><div class="VAS-047-cash-in-dialog-title" id="' + titleId + '">' + escapeHtml(lbl('VAS_048_DialogTitle', "Today's Cash Disbursements")) + '</div><div class="VAS-047-cash-in-dialog-subtitle">' + escapeHtml(lbl('VAS_048_DialogSubtitle', 'Completed cash journal disbursements recorded today')) + '</div></div>' +
                '<button type="button" class="VAS-047-cash-in-dialog-close" aria-label="' + escapeHtml(lbl('VAS_048_Close', 'Close')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>' +
                '</div>' +
                '<div class="VAS-047-cash-in-dialog-body"><div class="VAS-047-cash-in-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="VAS-047-cash-in-dialog-table"><thead><tr>' +
                '<th class="VAS-047-cash-in-th-document" style="text-align:left">' + escapeHtml(lbl('VAS_048_DocumentNo', 'Document No.')) + '</th>' +
                '<th class="VAS-047-cash-in-th-date" style="text-align:left">' + escapeHtml(lbl('VAS_048_Date', 'Date')) + '</th>' +
                '<th class="VAS-047-cash-in-th-type" style="text-align:left">' + escapeHtml(lbl('VAS_048_CashType', 'Cash Type')) + '</th>' +
                '<th class="VAS-047-cash-in-th-charge" style="text-align:left">' + escapeHtml(lbl('VAS_048_Charge', 'Charge')) + '</th>' +
                '<th class="VAS-047-cash-in-th-book" style="text-align:left">' + escapeHtml(lbl('VAS_048_CashBook', 'Cash Book')) + '</th>' +
                '<th class="VAS-047-cash-in-th-amount" style="text-align:left">' + escapeHtml(lbl('VAS_048_Amount', 'Amount')) + '</th>' +
                '<th class="VAS-047-cash-in-th-description" style="text-align:left">' + escapeHtml(lbl('VAS_048_DialogDescription', 'Description')) + '</th>' +
                '</tr></thead><tbody class="VAS-047-cash-in-dialog-tbody"></tbody></table></div>' +
                '<div class="VAS-047-cash-in-dialog-footer"><span class="VAS-047-cash-in-pager-helper"></span><div class="VAS-047-cash-in-dialog-actions"><div class="VAS-047-cash-in-pager">' +
                '<button type="button" class="VAS-047-cash-in-pager-btn VAS-047-cash-in-pager-prev" aria-label="' + escapeHtml(lbl('VAS_048_Previous', 'Previous')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                '<span class="VAS-047-cash-in-pager-text"></span>' +
                '<button type="button" class="VAS-047-cash-in-pager-btn VAS-047-cash-in-pager-next" aria-label="' + escapeHtml(lbl('VAS_048_Next', 'Next')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"/></svg></button>' +
                '</div><button type="button" class="VAS-047-cash-in-dialog-close-action">' + escapeHtml(lbl('VAS_048_Close', 'Close')) + '</button></div></div>' +
                '</div></div>'
            );

            $dialogTbody = $dialog.find('.VAS-047-cash-in-dialog-tbody');
            $dialogBusy = $dialog.find('.VAS-047-cash-in-dialog-busy');
            $pagerHelper = $dialog.find('.VAS-047-cash-in-pager-helper');
            $pagerPrev = $dialog.find('.VAS-047-cash-in-pager-prev');
            $pagerNext = $dialog.find('.VAS-047-cash-in-pager-next');
            $pagerText = $dialog.find('.VAS-047-cash-in-pager-text');
            showDialogBusy(false);

            $dialog.find('.VAS-047-cash-in-dialog-close, .VAS-047-cash-in-dialog-close-action, .VAS-047-cash-in-dialog-scrim').on('click', closeDialog);
            $pagerPrev.on('click', function () { if (!rowsLoading && pageNo > 1) { pageNo--; loadDialogRows(); } });
            $pagerNext.on('click', function () { if (!rowsLoading && pageNo < totalPages) { pageNo++; loadDialogRows(); } });

            eventNamespace = '.VAS048CashOut' + String(widgetId).replace(/[^A-Za-z0-9]/g, '');
            $(document).on('keydown' + eventNamespace, function (event) {
                if (event.key === 'Escape' && $dialog.is(':visible')) { closeDialog(); }
            });

            $('body').append($dialog);
        }

        function buildLayout() {
            var widgetId = $self.AD_UserHomeWidgetID;

            $root = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-root',
                'id': 'VAS_048_today-cash-out-cash-journal-root-' + widgetId
            });

            var $card = $('<section>', {
                'class': 'VAS_today-cash-out-cash-journal-card',
                'aria-label': lbl('VAS_048_CashOut', 'Cash out')
            });

            var $busy = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-busy vis-busyindicatorouterwrap',
                'id': 'VAS-gljtm-busy-' + widgetId,
                'html': '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
            });

            var $header = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-row'
            });

            var $title = $('<span>', {
                'class': 'VAS_today-cash-out-cash-journal-label',
                'id': 'VAS_048_today-cash-out-title-' + widgetId,
                'text': lbl('VAS_048_CashOut', 'Cash out')
            });

            var $date = $('<span>', {
                'class': 'VAS_today-cash-out-cash-journal-date',
                'id': 'VAS_048_today-cash-out-date-' + widgetId,
                'text': lbl('VAS_048_Today', 'Today')
            });

            var $value = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-value',
                'id': 'VAS_048_today-cash-out-value-' + widgetId
            });

            renderCurrencyAmount($value, 0);

            var $footer = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-footer'
            });

            var $delta = $('<span>', {
                'class': 'VAS_today-cash-out-cash-journal-delta VAS_today-cash-out-cash-journal-delta-down',
                'id': 'VAS_048_today-cash-out-delta-' + widgetId
            });

            var $icon = $(
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<polyline points="6 9 12 15 18 9"></polyline>' +
                '</svg>'
            );

            var $deltaText = $('<span>', {
                'id': 'VAS_048_today-cash-out-delta-text-' + widgetId,
                'text': formatPercent(0)
            });

            var $description = $('<span>', {
                'class': 'VAS_today-cash-out-cash-journal-description',
                'id': 'VAS_048_today-cash-out-description-' + widgetId,
                'text': lbl('VAS_048_VsSevenDayAvg', 'vs 7-day avg') + ' · 0 ' + lbl('VAS_048_Disbursements', 'disbursements')
            });

            var $state = $('<div>', {
                'class': 'VAS_today-cash-out-cash-journal-state',
                'id': 'VAS_048_today-cash-out-state-' + widgetId
            });

            var $action = $('<button>', {
                'type': 'button',
                'class': 'VAS_today-cash-out-cash-journal-action',
                'aria-label': lbl('VAS_048_DialogTitle', "Today's Cash Disbursements")
            });

            $action.on('click', openDialog);

            $delta.append($icon).append($deltaText);
            $footer.append($delta).append($description);
            $header.append($title).append($date);
            $card.append($busy).append($header).append($value).append($footer).append($state).append($action);
            $root.append($card);
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            $root.find('#VAS_048_today-cash-out-state-' + $self.AD_UserHomeWidgetID)
                .text(message || '')
                .addClass('is-visible');

            $root.find('#VAS_048_today-cash-out-value-' + $self.AD_UserHomeWidgetID)
                .text('')
                .hide();

            $root.find('#VAS_048_today-cash-out-delta-text-' + $self.AD_UserHomeWidgetID)
                .text('');

            $root.find('#VAS_048_today-cash-out-description-' + $self.AD_UserHomeWidgetID)
                .text('');

            $root.find('.VAS_today-cash-out-cash-journal-footer').hide();
        }

        function renderData(data) {
            if (!$root || isDisposed) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var title = data.title || lbl('VAS_048_CashOut', 'Cash out');
            var dateText = data.badgeText || lbl('VAS_048_Today', 'Today');
            var amount = safeNumber(data.mainMetric);
            var deltaPercent = safeNumber(data.deltaPercent);
            var disbursementCount = safeNumber(data.disbursementCount);
            dialogCurrencyLabel = data.currencySymbol || data.currencyISO || '';
            var footerText = lbl('VAS_048_VsSevenDayAvg', 'vs 7-day avg') + ' · ' + disbursementCount.toLocaleString(window.navigator.language) + ' ' + lbl('VAS_048_Disbursements', 'disbursements');

            $root.find('#VAS_048_today-cash-out-state-' + widgetId).removeClass('is-visible').text('');
            $root.find('#VAS_048_today-cash-out-title-' + widgetId).text(title);
            $root.find('#VAS_048_today-cash-out-date-' + widgetId).text(dateText);
            renderCurrencyAmount($root.find('#VAS_048_today-cash-out-value-' + widgetId), amount, data.currencySymbol, data.currencyISO, data.stdPrecision);
            $root.find('#VAS_048_today-cash-out-value-' + widgetId).show();
            $root.find('.VAS_today-cash-out-cash-journal-footer').show();
            $root.find('#VAS_048_today-cash-out-delta-text-' + widgetId).text(formatPercent(deltaPercent));
            $root.find('#VAS_048_today-cash-out-description-' + widgetId).text(footerText);

            var $delta = $root.find('#VAS_048_today-cash-out-delta-' + widgetId);
            $delta.removeClass('VAS_today-cash-out-cash-journal-delta-up VAS_today-cash-out-cash-journal-delta-down');

            if (deltaPercent > 0) {
                $delta.addClass('VAS_today-cash-out-cash-journal-delta-up');
            } else {
                $delta.addClass('VAS_today-cash-out-cash-journal-delta-down');
            }
        }

        function loadData() {
            if (!$root || isDisposed) {
                return;
            }

            if (ajaxRequest && ajaxRequest.readyState !== 4) {
                ajaxRequest.abort();
            }

            showBusy(true);

            ajaxRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_048_TodayCashOutCashJournal/GetTodayCashOut',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    if (!response) {
                        setState(lbl('VAS_048_LoadError', 'Unable to load cash out'));
                        return;
                    }

                    if (response.error === 'VAS_048_SessionExpired') {
                        setState(lbl('VAS_048_SessionExpired', 'Session Expired'));
                        return;
                    }

                    if (response.success === false || response.error) {
                        setState(response.error || lbl('VAS_048_LoadError', 'Unable to load cash out'));
                        return;
                    }

                    if (response.hasData === false) {
                        setState(lbl('VAS_048_NoData', 'No data'));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_048_LoadError', 'Unable to load cash out'));
                    }
                },
                complete: function () {
                    if (!isDisposed && $root) {
                        showBusy(false);
                    }
                }
            });
        }

        this.initalize = function () {
            buildLayout();
            createDialog();
            loadData();
        };

        this.refreshWidget = function () {
            loadData();
        };

        this.disposeComponent = function () {
            isDisposed = true;

            if (ajaxRequest && ajaxRequest.readyState !== 4) {
                ajaxRequest.abort();
            }

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
                $root.remove();
            }

            ajaxRequest = null;
            rowsRequest = null;
            $dialog = null;
            $root = null;
            $self = null;
        };

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_048_TodayCashOutCashJournalWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;

        if (frame && frame.widgetInfo) {
            this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        }

        if (!this.AD_UserHomeWidgetID) {
            this.AD_UserHomeWidgetID = windowNo || new Date().getTime();
        }

        this.initalize();

        if (this.frame && this.frame.getContentGrid) {
            this.frame.getContentGrid().append(this.getRoot());
        }
    };

    VAS.VAS_048_TodayCashOutCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_048_TodayCashOutCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
