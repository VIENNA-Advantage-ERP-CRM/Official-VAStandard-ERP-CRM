/**
 * Current Cash — Cash Journal
 * Purpose - Shows the latest ending balance for the selected Cash Book / Drawer.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Current cash                         | VAS_050_CurrentCash
 *  2  | Drawer                               | VAS_050_Drawer
 *  3  | Live                                 | VAS_050_Live
 *  4  | Loading                              | VAS_050_Loading
 *  5  | No data                              | VAS_050_NoData
 *  6  | Unable to load current cash          | VAS_050_LoadError
 *  7  | short of float                       | VAS_050_ShortOfFloat
 *  8  | cash on hand                         | VAS_050_CashOnHand
 *  9  | no cash left                         | VAS_050_NoCashLeft
 * 10  | Session Expired                      | VAS_050_SessionExpired
 * 11  | Cash Book Balance History            | VAS_050_DialogTitle
 * 12  | Journals for the selected cash book  | VAS_050_DialogSubtitle
 * 13  | Document No.                         | VAS_050_DocumentNo
 * 14  | Beginning Balance                    | VAS_050_BeginningBalance
 * 15  | Ending Balance                       | VAS_050_EndingBalance
 * 16  | Status                               | VAS_050_Status
 * 17  | Statement Difference                 | VAS_050_StatementDifference
 * 18  | Currency                             | VAS_050_Currency
 * 19  | Close                                | VAS_050_Close
 * 20  | No cash journals found               | VAS_050_NoJournals
 * 21  | journal                              | VAS_050_Journal
 * 22  | journals                             | VAS_050_Journals
 * 23  | Showing                              | VAS_050_Showing
 * 24  | of                                   | VAS_050_Of
 * 25  | Previous                             | VAS_050_Previous
 * 26  | Next                                 | VAS_050_Next
 * ─────────────────────────────────────────────────────────────────────
 */


; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_050_CurrentCashForCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;
        var rowsRequest = null;
        var selectedCashBookId = 0;
        var $dialog = null;
        var $dialogTbody = null;
        var $dialogBusy = null;
        var $pagerHelper = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;
        var $previousFocus = null;
        var rowsLoading = false;
        var pageNo = 1;
        var pageSize = 6;
        var totalPages = 0;
        var totalRecords = 0;
        var eventNamespace = '';

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== key && text !== '[' + key + ']' ? text : fallback;
        }

        function escapeHtml(value) {
            return $('<div>').text(value === null || value === undefined ? '' : String(value)).html();
        }

        function getPrecision(precision) {
            var stdPrecision = Number(precision);

            return isNaN(stdPrecision) || stdPrecision < 0 ? 2 : stdPrecision;
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO, precision) {
            var numericValue = Number(value || 0);
            var stdPrecision = getPrecision(precision);

            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            var sign = numericValue < 0 ? '-' : '';

            if (currencySymbol) {
                return sign + currencySymbol + amount;
            }

            return currencyISO ? sign + currencyISO + amount : sign + amount;
        }

        function getCurrencyLabel(currencySymbol, currencyISO) {
            return currencySymbol || currencyISO || '';
        }

        function getAmountParts(value, currencySymbol, currencyISO, precision, signPrefix) {
            var numericValue = Number(value || 0);
            var stdPrecision = getPrecision(precision);
            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
            var decimalMatch = amount.match(/([.,]\d+)$/);

            return {
                prefix: signPrefix + getCurrencyLabel(currencySymbol, currencyISO),
                main: decimalMatch ? amount.substring(0, amount.length - decimalMatch[1].length) : amount,
                decimal: decimalMatch ? decimalMatch[1] : ''
            };
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljtm-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.addClass('is-visible'); } else { $b.removeClass('is-visible'); }
        }

        function safeNumber(value) {
            var numberValue = Number(value || 0);
            return isNaN(numberValue) ? 0 : numberValue;
        }

        function formatAmount(value, precision) {
            return safeNumber(value).toLocaleString(window.navigator.language, {
                minimumFractionDigits: getPrecision(precision),
                maximumFractionDigits: getPrecision(precision)
            });
        }

        function formatSignedAmount(value, currencySymbol, currencyISO, precision, showPositiveSign) {
            var numberValue = safeNumber(value);
            var sign = '';

            if (numberValue < 0) {
                sign = '−';
            } else if (numberValue > 0 && showPositiveSign === true) {
                sign = '+';
            }

            return sign + formatCurrencyAmount(Math.abs(numberValue), currencySymbol, currencyISO, precision);
        }

        function renderSignedAmount($target, value, currencySymbol, currencyISO, precision, showPositiveSign) {
            var numberValue = safeNumber(value);
            var sign = '';

            if (numberValue < 0) {
                sign = '−';
            } else if (numberValue > 0 && showPositiveSign === true) {
                sign = '+';
            }

            var parts = getAmountParts(Math.abs(numberValue), currencySymbol, currencyISO, precision, sign);

            $target.empty()
                .append($('<span>', { 'class': 'VAS-cash-amount-prefix', 'text': parts.prefix }))
                .append($('<span>', { 'class': 'VAS-cash-amount-main', 'text': parts.main }))
                .append($('<span>', { 'class': 'VAS-cash-amount-decimal', 'text': parts.decimal }));
        }

        function getStatusText(balance) {
            var numberValue = safeNumber(balance);

            if (numberValue < 0) {
                return lbl('VAS_050_ShortOfFloat', 'short of float');
            }

            if (numberValue > 0) {
                return lbl('VAS_050_CashOnHand', 'cash on hand');
            }

            return lbl('VAS_050_NoCashLeft', 'no cash left');
        }

        function getStateClass(value, prefix) {
            var numberValue = safeNumber(value);

            if (numberValue > 0) {
                return prefix + '-positive';
            }

            if (numberValue < 0) {
                return prefix + '-negative';
            }

            return prefix + '-neutral';
        }

        function getTrendIcon(value) {
            var numberValue = safeNumber(value);

            if (numberValue > 0) {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="18 15 12 9 6 15"></polyline></svg>';
            }

            if (numberValue < 0) {
                return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="6 9 12 15 18 9"></polyline></svg>';
            }

            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="6" y1="12" x2="18" y2="12"></line></svg>';
        }

        function showDialogBusy(show) {
            if ($dialogBusy && $dialogBusy[0]) {
                $dialogBusy[0].style.visibility = show ? 'visible' : 'hidden';
            }
        }

        function renderDialogMessage(message) {
            if ($dialogTbody) {
                $dialogTbody.html('<tr><td class="VAS-047-cash-in-dialog-empty" colspan="7">' + escapeHtml(message) + '</td></tr>');
            }
        }

        function getStatusTone(value) {
            value = String(value || '').toUpperCase();
            if (value === 'CO' || value === 'CL') { return ' is-success'; }
            if (value === 'DR' || value === 'IP') { return ' is-info'; }
            if (value === 'VO' || value === 'RE') { return ' is-danger'; }
            return ' is-warning';
        }

        function renderDialogRows(rows) {
            $dialogTbody.empty();

            if (!rows || !rows.length) {
                renderDialogMessage(lbl('VAS_050_NoJournals', 'No cash journals found'));
                return;
            }

            $.each(rows, function (index, row) {
                var documentNo = row.documentNo || '-';
                var cashDate = row.statementDate || '-';
                var cashType = row.cashTypeName || row.cashTypeValue || '-';
                var charge = row.chargeName || '-';
                var currencyLabel = row.currencyISO || row.currencySymbol || '';
                var amountText = formatAmount(row.amount, row.stdPrecision);
                var amountWithCurrency = currencyLabel ? currencyLabel + ' ' + amountText : amountText;
                var status = row.docStatusName || row.docStatusValue || '-';
                var description = row.description || '-';

                $dialogTbody.append(
                    '<tr>' +
                    '<td class="VAS-050-current-cash-td-document" style="text-align:left" title="' + escapeHtml(documentNo) + '"><span class="VAS-047-cash-in-truncate">' + escapeHtml(documentNo) + '</span></td>' +
                    '<td class="VAS-050-current-cash-td-date" style="text-align:left" title="' + escapeHtml(cashDate) + '">' + escapeHtml(cashDate) + '</td>' +
                    '<td class="VAS-050-current-cash-td-type" style="text-align:left" title="' + escapeHtml(cashType) + '"><span class="VAS-047-cash-in-type-pill">' + escapeHtml(cashType) + '</span></td>' +
                    '<td class="VAS-050-current-cash-td-charge" style="text-align:left" title="' + escapeHtml(charge) + '"><span class="VAS-047-cash-in-truncate">' + escapeHtml(charge) + '</span></td>' +
                    '<td class="VAS-050-current-cash-td-amount" style="text-align:left" title="' + escapeHtml(amountWithCurrency) + '">' + escapeHtml(amountWithCurrency) + '</td>' +
                    '<td class="VAS-050-current-cash-td-status" style="text-align:left" title="' + escapeHtml(status) + '"><span class="VAS-050-current-cash-status' + getStatusTone(row.docStatusValue) + '">' + escapeHtml(status) + '</span></td>' +
                    '<td class="VAS-050-current-cash-td-description" style="text-align:left" title="' + escapeHtml(description) + '"><span class="VAS-047-cash-in-truncate">' + escapeHtml(description) + '</span></td>' +
                    '</tr>'
                );
            });
        }

        function updateDialogPager() {
            var from = totalRecords ? ((pageNo - 1) * pageSize) + 1 : 0;
            var to = Math.min(pageNo * pageSize, totalRecords);

            if ($pagerHelper) {
                $pagerHelper.text(totalRecords ?
                    lbl('VAS_050_Showing', 'Showing') + ' ' + from + '\u2013' + to + ' ' +
                    lbl('VAS_050_Of', 'of') + ' ' + totalRecords + ' ' +
                    lbl(totalRecords === 1 ? 'VAS_050_Journal' : 'VAS_050_Journals', totalRecords === 1 ? 'journal' : 'journals') : '');
            }
            if ($pagerText) {
                $pagerText.text(totalPages ? pageNo + ' ' + lbl('VAS_050_Of', 'of') + ' ' + totalPages : '');
            }
            if ($pagerPrev) { $pagerPrev.prop('disabled', rowsLoading || pageNo <= 1); }
            if ($pagerNext) { $pagerNext.prop('disabled', rowsLoading || totalPages <= 1 || pageNo >= totalPages); }
        }

        function loadDialogRows() {
            if (!$dialogTbody || rowsLoading || selectedCashBookId <= 0) {
                if (selectedCashBookId <= 0) { renderDialogMessage(lbl('VAS_050_NoJournals', 'No cash journals found')); }
                return;
            }

            rowsLoading = true;
            showDialogBusy(true);
            updateDialogPager();
            rowsRequest = $.ajax({
                url: VIS.Application.contextUrl + 'VAS/VAS_050_CurrentCashForCashJournal/GetCurrentCashRows',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: { cashBookId: selectedCashBookId, pageNo: pageNo, pageSize: pageSize },
                success: function (response) {
                    if (!response || response.success === false || response.error) {
                        renderDialogMessage(response && response.error === 'VAS_050_SessionExpired' ?
                            lbl('VAS_050_SessionExpired', 'Session Expired') :
                            (response && response.error) || lbl('VAS_050_LoadError', 'Could not load data'));
                        return;
                    }
                    pageNo = Number(response.pageNo || pageNo);
                    totalPages = Number(response.totalPages || 0);
                    totalRecords = Number(response.totalRecords || 0);
                    renderDialogRows(response.rows || []);
                },
                error: function () { renderDialogMessage(lbl('VAS_050_LoadError', 'Could not load data')); },
                complete: function () {
                    rowsLoading = false;
                    rowsRequest = null;
                    showDialogBusy(false);
                    updateDialogPager();
                }
            });
        }

        function openDialog() {
            if (!$dialog) { return; }
            $previousFocus = $(document.activeElement);
            pageNo = 1;
            totalPages = 0;
            totalRecords = 0;
            $dialog.css('display', 'flex');
            $('body').addClass('VAS-047-cash-in-body-lock');
            $dialog.find('.VAS-047-cash-in-dialog-close').trigger('focus');
            loadDialogRows();
        }

        function closeDialog() {
            if (!$dialog) { return; }
            if (rowsRequest && rowsRequest.readyState !== 4) { rowsRequest.abort(); }
            $dialog.hide();
            $('body').removeClass('VAS-047-cash-in-body-lock');
            if ($previousFocus && $previousFocus.length) { $previousFocus.trigger('focus'); }
            $previousFocus = null;
        }

        function createDialog() {
            var uid = $self.AD_UserHomeWidgetID;
            var titleId = 'VAS-050-current-cash-dialog-title-' + uid;
            var heading = function (key, fallback, cssClass) {
                var text = escapeHtml(lbl(key, fallback));
                return '<th class="' + cssClass + '" title="' + text + '" style="text-align:left">' + text + '</th>';
            };

            $dialog = $(
                '<div class="VAS-047-cash-in-dialog VAS-050-current-cash-dialog" style="display:none" role="dialog" aria-modal="true" aria-labelledby="' + titleId + '">' +
                '<div class="VAS-047-cash-in-dialog-scrim"></div><div class="VAS-047-cash-in-dialog-card">' +
                '<div class="VAS-047-cash-in-dialog-header"><div class="VAS-047-cash-in-dialog-icon" aria-hidden="true">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="M3 10h18"/><path d="M8 15h3"/></svg></div>' +
                '<div class="VAS-047-cash-in-dialog-title-group"><div class="VAS-047-cash-in-dialog-title" id="' + titleId + '">' + escapeHtml(lbl('VAS_050_DialogTitle', 'Cash Book Balance History')) + '</div>' +
                '<div class="VAS-047-cash-in-dialog-subtitle">' + escapeHtml(lbl('VAS_050_DialogSubtitle', 'Journals for the selected cash book')) + '</div></div>' +
                '<button type="button" class="VAS-047-cash-in-dialog-close" aria-label="' + escapeHtml(lbl('VAS_050_Close', 'Close')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button></div>' +
                '<div class="VAS-047-cash-in-dialog-body"><div class="VAS-047-cash-in-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>' +
                '<table class="VAS-047-cash-in-dialog-table VAS-050-current-cash-table"><thead><tr>' +
                heading('VAS_050_DocumentNo', 'Document No.', 'VAS-050-current-cash-th-document') +
                heading('VAS_050_CashDate', 'Cash Date', 'VAS-050-current-cash-th-date') +
                heading('VAS_050_CashType', 'Cash Type', 'VAS-050-current-cash-th-type') +
                heading('VAS_050_Charge', 'Charge', 'VAS-050-current-cash-th-charge') +
                heading('VAS_050_Amount', 'Amount', 'VAS-050-current-cash-th-amount') +
                heading('VAS_050_Status', 'Status', 'VAS-050-current-cash-th-status') +
                heading('VAS_050_Description', 'Description', 'VAS-050-current-cash-th-description') +
                '</tr></thead><tbody class="VAS-047-cash-in-dialog-tbody"></tbody></table></div>' +
                '<div class="VAS-047-cash-in-dialog-footer"><span class="VAS-047-cash-in-pager-helper"></span><div class="VAS-047-cash-in-dialog-actions">' +
                '<div class="VAS-047-cash-in-pager"><button type="button" class="VAS-047-cash-in-pager-btn VAS-047-cash-in-pager-prev" aria-label="' + escapeHtml(lbl('VAS_050_Previous', 'Previous')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 18 9 12 15 6"/></svg></button>' +
                '<span class="VAS-047-cash-in-pager-text"></span><button type="button" class="VAS-047-cash-in-pager-btn VAS-047-cash-in-pager-next" aria-label="' + escapeHtml(lbl('VAS_050_Next', 'Next')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"/></svg></button></div>' +
                '<button type="button" class="VAS-047-cash-in-dialog-close-action">' + escapeHtml(lbl('VAS_050_Close', 'Close')) + '</button></div></div></div></div>'
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
            eventNamespace = '.VAS050CurrentCash' + String(uid).replace(/[^A-Za-z0-9]/g, '');
            $(document).on('keydown' + eventNamespace, function (event) { if (event.key === 'Escape' && $dialog.is(':visible')) { closeDialog(); } });
            $('body').append($dialog);
        }

        function buildLayout() {
            var widgetId = $self.AD_UserHomeWidgetID;

            $root = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-root',
                'id': 'VAS_050_current-cash-cash-journal-root-' + widgetId
            });

            var $card = $('<section>', {
                'class': 'VAS_current-cash-cash-journal-card',
                'aria-label': lbl('VAS_050_CurrentCash', 'Current cash')
            });

            var $action = $('<button>', {
                'type': 'button',
                'class': 'VAS_current-cash-cash-journal-action',
                'aria-label': lbl('VAS_050_DialogTitle', 'Cash Book Balance History')
            });

            var $busy = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-busy vis-busyindicatorouterwrap',
                'id': 'VAS-gljtm-busy-' + widgetId,
                'html': '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
            });

            var $header = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-row'
            });

            var $title = $('<span>', {
                'class': 'VAS_current-cash-cash-journal-label',
                'id': 'VAS_050_current-cash-title-' + widgetId,
                'text': lbl('VAS_050_CurrentCash', 'Current cash')
            });

            var $filter = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-filter'
            });

            var $select = $('<select>', {
                'class': 'VAS_current-cash-cash-journal-select',
                'id': 'VAS_050_current-cash-cashbook-' + widgetId,
                'aria-label': lbl('VAS_050_Drawer', 'Drawer')
            });

            var $separator = $('<span>', {
                'class': 'VAS_current-cash-cash-journal-filter-separator',
                'text': '·'
            });

            var $live = $('<span>', {
                'class': 'VAS_current-cash-cash-journal-live',
                'text': lbl('VAS_050_Live', 'Live')
            });

            var $value = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-value VAS_current-cash-cash-journal-value-neutral',
                'id': 'VAS_050_current-cash-value-' + widgetId
            });

            renderSignedAmount($value, 0);

            var $footer = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-footer'
            });

            var $delta = $('<span>', {
                'class': 'VAS_current-cash-cash-journal-delta VAS_current-cash-cash-journal-delta-neutral',
                'id': 'VAS_050_current-cash-delta-' + widgetId
            });

            var $deltaText = $('<span>', {
                'id': 'VAS_050_current-cash-delta-text-' + widgetId,
                'text': formatCurrencyAmount(0)
            });

            var $description = $('<span>', {
                'class': 'VAS_current-cash-cash-journal-description',
                'id': 'VAS_050_current-cash-description-' + widgetId,
                'text': getStatusText(0)
            });

            var $cashDate = $('<span>', {
                'class': 'VAS_current-cash-cash-journal-cash-date',
                'id': 'VAS_050_current-cash-cash-date-' + widgetId
            });

            var $state = $('<div>', {
                'class': 'VAS_current-cash-cash-journal-state',
                'id': 'VAS_050_current-cash-state-' + widgetId
            });

            $delta.html(getTrendIcon(0)).append($deltaText);
            $footer.append($delta).append($description).append($cashDate);
            $filter.append($select).append($separator).append($live);
            $header.append($title).append($filter);
            $card.append($action).append($busy).append($header).append($value).append($footer).append($state);
            $root.append($card);

            $action.on('click', openDialog);

            $select.on('change', function () {
                selectedCashBookId = Number($(this).val() || 0);
                loadData();
            });

            createDialog();
        }

        function renderCashBookOptions(cashBooks) {
            var widgetId = $self.AD_UserHomeWidgetID;
            var $select = $root.find('#VAS_050_current-cash-cashbook-' + widgetId);

            $select.empty();

            if (!cashBooks || !cashBooks.length) {
                $('<option>', {
                    value: '0',
                    text: lbl('VAS_050_Drawer', 'Drawer')
                }).appendTo($select);
                return;
            }

            $.each(cashBooks, function (index, cashBook) {
                $('<option>', {
                    value: cashBook.cCashBookId || cashBook.C_CashBook_ID || 0,
                    text: cashBook.name || lbl('VAS_050_Drawer', 'Drawer')
                }).appendTo($select);
            });

            if (selectedCashBookId > 0) {
                $select.val(String(selectedCashBookId));
            }
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;

            $root.find('#VAS_050_current-cash-state-' + widgetId)
                .text(message || '')
                .addClass('is-visible');

            $root.find('#VAS_050_current-cash-value-' + widgetId)
                .removeClass('VAS_current-cash-cash-journal-value-positive VAS_current-cash-cash-journal-value-negative VAS_current-cash-cash-journal-value-neutral')
                .addClass('VAS_current-cash-cash-journal-value-neutral')
                .text('')
                .hide();

            $root.find('#VAS_050_current-cash-delta-' + widgetId)
                .removeClass('VAS_current-cash-cash-journal-delta-positive VAS_current-cash-cash-journal-delta-negative VAS_current-cash-cash-journal-delta-neutral')
                .addClass('VAS_current-cash-cash-journal-delta-neutral')
                .html(getTrendIcon(0))
                .append($('<span>', {
                    'id': 'VAS_050_current-cash-delta-text-' + widgetId,
                    'text': ''
                }));

            $root.find('#VAS_050_current-cash-description-' + widgetId)
                .text('');

            $root.find('#VAS_050_current-cash-cash-date-' + widgetId)
                .text('');

            $root.find('.VAS_current-cash-cash-journal-footer').hide();
        }

        function renderData(data) {
            if (!$root || isDisposed) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var title = data.cashBookName || data.title || lbl('VAS_050_CurrentCash', 'Current cash');
            var balance = safeNumber(data.mainMetric);
            var footerAmount = data.footerAmount !== null && data.footerAmount !== undefined ? safeNumber(data.footerAmount) : Math.abs(balance);
            var valueClass = getStateClass(balance, 'VAS_current-cash-cash-journal-value');
            var deltaClass = getStateClass(balance, 'VAS_current-cash-cash-journal-delta');

            if (data.cCashBookId) {
                selectedCashBookId = Number(data.cCashBookId);
            }

            renderCashBookOptions(data.cashBooks || []);

            $root.find('#VAS_050_current-cash-state-' + widgetId).removeClass('is-visible').text('');
            $root.find('#VAS_050_current-cash-title-' + widgetId).text(title);

            $root.find('#VAS_050_current-cash-value-' + widgetId)
                .removeClass('VAS_current-cash-cash-journal-value-positive VAS_current-cash-cash-journal-value-negative VAS_current-cash-cash-journal-value-neutral')
                .addClass(valueClass)
                .show();
            renderSignedAmount($root.find('#VAS_050_current-cash-value-' + widgetId), balance, data.currencySymbol, data.currencyISO, data.stdPrecision, false);

            $root.find('.VAS_current-cash-cash-journal-footer').show();

            $root.find('#VAS_050_current-cash-delta-' + widgetId)
                .removeClass('VAS_current-cash-cash-journal-delta-positive VAS_current-cash-cash-journal-delta-negative VAS_current-cash-cash-journal-delta-neutral')
                .addClass(deltaClass)
                .html(getTrendIcon(balance))
                .append($('<span>', {
                    'id': 'VAS_050_current-cash-delta-text-' + widgetId,
                    'text': formatSignedAmount(balance < 0 ? 0 - footerAmount : footerAmount, data.currencySymbol, data.currencyISO, data.stdPrecision, true)
                }));

            $root.find('#VAS_050_current-cash-description-' + widgetId)
                .text(data.description || getStatusText(balance));

            $root.find('#VAS_050_current-cash-cash-date-' + widgetId)
                .text(data.statementDate || '');
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
                url: VIS.Application.contextUrl + 'VAS/VAS_050_CurrentCashForCashJournal/GetCurrentCash',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: {
                    cashBookId: selectedCashBookId || 0
                },
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    if (!response) {
                        setState(lbl('VAS_050_LoadError', 'Unable to load current cash'));
                        return;
                    }

                    if (response.error === 'VAS_050_SessionExpired') {
                        setState(lbl('VAS_050_SessionExpired', 'Session Expired'));
                        return;
                    }

                    if (response.success === false || response.error) {
                        setState(response.error || lbl('VAS_050_LoadError', 'Unable to load current cash'));
                        return;
                    }

                    if (response.hasData === false) {
                        renderCashBookOptions(response.cashBooks || []);
                        setState(lbl('VAS_050_NoData', 'No data'));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_050_LoadError', 'Unable to load current cash'));
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
            $dialogTbody = null;
            $root = null;
            $self = null;
        };

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_050_CurrentCashForCashJournalWidget.prototype.init = function (windowNo, frame) {
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

    VAS.VAS_050_CurrentCashForCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_050_CurrentCashForCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
