/**
 * Upcoming runs
 * Purpose - Displays upcoming AP payment runs due within the next 7 days, grouped by payment method and due date.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Upcoming runs                        | VAS_031_MessageUpcomingRuns
 *  2  | Next 7 days                          | VAS_031_MessageNext7Days
 *  3  | payment                              | VAS_031_MessagePayment
 *  4  | payments                             | VAS_031_MessagePayments
 *  5  | Loading                              | VAS_031_MessageLoading
 *  6  | No Data                              | VAS_031_MessageNoData
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_031_UpcomingAPRunsWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $root = $('<div class="vas-upcoming-ap-runs-root">');
        var $card;
        var $body;
        var $pager;
        var $pagerPrev;
        var $pagerNext;
        var $pagerText;
        var $busy;
        var $state;
        var $payDialog;
        var $payDialogTitle;
        var $payDialogSub;
        var $payDialogNotice;
        var $payDialogGrid;
        var $payDialogSave;
        var $payDialogSaveLabel;
        var $payDialogBusy;

        var isDisposed = false;
        var runsData = [];
        var selectedRun = null;
        var selectedPaymentRow = null;
        var paymentRows = [];
        var popupLookups = null;
        var saveInProgress = false;
        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            $card = $('<div class="vas-upcoming-ap-runs-card">');

            var $head = $('<div class="vas-upcoming-ap-runs-head">');
            var $headLeft = $('<div class="vas-upcoming-ap-runs-head-left">');
            var $titleRow = $('<div class="vas-upcoming-ap-runs-title-row">');

            var $iconBox = $('<span class="vas-upcoming-ap-runs-icon-box">');
            var $icon = $(
                '<svg class="vas-upcoming-ap-runs-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<circle cx="12" cy="12" r="10"></circle>' +
                '<polyline points="12 6 12 12 16 14"></polyline>' +
                '</svg>'
            );

            var $title = $('<div class="vas-upcoming-ap-runs-title">').text(lbl('VAS_031_MessageUpcomingRuns', 'Upcoming runs'));
            var $sub = $('<div class="vas-upcoming-ap-runs-sub">').text(lbl('VAS_031_MessageNext7Days', 'Next 7 days'));

            $pager = $('<div class="vas-upcoming-ap-runs-pager">');
            $pagerPrev = $('<button type="button" class="vas-upcoming-ap-runs-page-btn" aria-label="' + lbl('VAS_Previous', 'Previous') + '">‹</button>');
            $pagerText = $('<span class="vas-upcoming-ap-runs-page-text">');
            $pagerNext = $('<button type="button" class="vas-upcoming-ap-runs-page-btn" aria-label="' + lbl('VAS_Next', 'Next') + '">›</button>');

            $pager.append($pagerPrev).append($pagerText).append($pagerNext);

            $iconBox.append($icon);
            $titleRow.append($iconBox).append($title);
            $headLeft.append($titleRow).append($sub);

            $head.append($headLeft).append($pager);

            $body = $('<div class="vas-upcoming-ap-runs-body">');
            $busy = $('<div class="vas-upcoming-ap-runs-busy">').text(lbl('VAS_031_MessageLoading', 'Loading'));
            $state = $('<div class="vas-upcoming-ap-runs-state-message">');

            $card.append($head).append($body).append($busy).append($state);
            $root.empty().append($card);
            createPayDialog();

            $pagerPrev.on('click', function () {
                if (pageNo <= 1) {
                    return;
                }

                pageNo--;
                renderPage();
            });

            $pagerNext.on('click', function () {
                if (totalPages <= 1 || pageNo >= totalPages) {
                    return;
                }

                pageNo++;
                renderPage();
            });
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            showBusy(true);
            showState(false, '');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_033_UpcomingAPRunsWidget/GetUpcomingAPRuns',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = response;

                    if (typeof response === 'string') {
                        try {
                            data = JSON.parse(response);
                        }
                        catch (e) {
                            showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                            return;
                        }
                    }

                    if (!data || data.error) {
                        showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                        return;
                    }

                    renderData(data);
                },
                error: function () {
                    if (!isDisposed) {
                        showState(true, lbl('VAS_ErrorLoading', 'Could not load data'));
                    }
                },
                complete: function () {
                    if (!isDisposed) {
                        showBusy(false);
                    }
                }
            });
        }

        function renderData(data) {
            runsData = $.isArray(data.runs)
                ? $.grep(data.runs, function (run) {
                    var amount = Number(pickRunValue(run, 'totalAmount', 'amount') || 0);
                    var paymentCount = Number(run.paymentCount || 0);

                    return (!isNaN(amount) && amount > 0) || (!isNaN(paymentCount) && paymentCount > 0);
                })
                : [];

            if (runsData.length === 0) {
                setNoData();
                return;
            }

            pageNo = 1;
            totalPages = Math.ceil(runsData.length / pageSize);
            renderPage();
        }

        function renderPage() {
            if (!runsData || runsData.length === 0) {
                setNoData();
                return;
            }

            totalPages = Math.max(1, Math.ceil(runsData.length / pageSize));

            if (pageNo < 1) {
                pageNo = 1;
            }

            if (pageNo > totalPages) {
                pageNo = totalPages;
            }

            showState(false, '');
            $body.empty();

            var startIndex = (pageNo - 1) * pageSize;
            var pageItems = runsData.slice(startIndex, startIndex + pageSize);

            for (var i = 0; i < pageItems.length; i++) {
                $body.append(createRunRow(pageItems[i]));
            }

            updatePager();
        }

        function updatePager() {
            if (!$pager) {
                return;
            }

            if ($pagerText) {
                if (totalPages > 1) {
                    $pagerText.text(pageNo + ' ' + lbl('VAS_Of', 'of') + ' ' + totalPages);
                }
                else {
                    $pagerText.text('');
                }
            }

            if ($pagerPrev) {
                $pagerPrev.prop('disabled', pageNo <= 1 || totalPages <= 1);
            }

            if ($pagerNext) {
                $pagerNext.prop('disabled', totalPages <= 1 || pageNo >= totalPages);
            }
        }

        function createRunRow(run) {
            var paymentMethodName = run.paymentMethodName || lbl('VAS_031_MessageNotSpecified', 'Not Specified');
            var paymentCount = Number(run.paymentCount || 0);
            var amount = Number(pickRunValue(run, 'totalAmount', 'amount') || 0);
            var dueDateText = pickRunValue(run, 'dueDateText', 'runDateText') || formatDate(pickRunValue(run, 'dueDate', 'runDate'));
            var titleText = getRunTitle(paymentMethodName, run.vendorName, paymentCount);
            var metaParts = [];
            var barClass = getPaymentMethodClass(paymentMethodName);

            if (dueDateText) {
                metaParts.push(dueDateText);
            }

            metaParts.push(paymentCount.toLocaleString(window.navigator.language) + ' ' + getPaymentLabel(paymentCount));

            var $row = $('<div class="vas-upcoming-ap-runs-row">');
            var $bar = $('<span class="vas-upcoming-ap-runs-bar">').addClass(barClass);
            var $info = $('<div class="vas-upcoming-ap-runs-info">');
            var $title = $('<div class="vas-upcoming-ap-runs-run-title">').text(titleText);
            var $meta = $('<div class="vas-upcoming-ap-runs-meta">').text(metaParts.join(' · '));
            var $amount = $('<span class="vas-upcoming-ap-runs-amount">').text(formatCurrencyAmount(amount, run.currencySymbol, run.currencyISO, run.stdPrecision));
            var $actions = $('<div class="vas-upcoming-ap-runs-actions">');
            var $payBtn = $('<button type="button" class="vas-upcoming-ap-runs-pay-btn">')
                .text(lbl('VAS_031_MessagePay', 'Pay'))
                .append($('<span class="vas-upcoming-ap-runs-pay-arrow">').text('›'));

            $info.append($title).append($meta);
            $actions.append($amount).append($payBtn);
            $row.append($bar).append($info).append($actions);

            $payBtn.on('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                openPayDialog(run);
            });

            return $row;
        }

        function openPayDialog(run) {
            if (!$payDialog || !run) {
                return;
            }

            selectedRun = run;
            selectedPaymentRow = null;
            paymentRows = [];

            var amountText = formatCurrencyAmount(
                pickRunValue(run, 'totalAmount', 'amount'),
                run.currencySymbol,
                run.currencyISO,
                run.stdPrecision
            );

            if ($payDialogTitle) {
                $payDialogTitle.text(lbl('VAS_031_MessageCreatePayment', 'Create Payment'));
            }

            if ($payDialogSub) {
                $payDialogSub.text(lbl('VAS_031_MessagePrefilledFromUpcoming', 'Pre-filled from upcoming') + ' · ' + (run.paymentMethodName || ''));
            }

            if ($payDialogNotice) {
                $payDialogNotice.text(lbl('VAS_031_MessageLoadingDetails', 'Loading payment details'));
            }

            if ($payDialogGrid) {
                $payDialogGrid.empty();
            }

            setPayDialogBusy(true, false);
            $payDialog.show();
            $('body').addClass('vas-upcoming-ap-runs-body-lock');

            ensurePopupLookups(function () {
                loadRunPaymentDetails(run, amountText);
            });
        }

        function closePayDialog() {
            if (!$payDialog || saveInProgress) {
                return;
            }

            selectedRun = null;
            $payDialog.hide();
            $('body').removeClass('vas-upcoming-ap-runs-body-lock');

            if ($payDialogGrid) {
                $payDialogGrid.empty();
            }
        }

        function ensurePopupLookups(callback) {
            if (popupLookups) {
                callback();
                return;
            }

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_033_UpcomingAPRunsWidget/GetPaymentPopupLookups',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    popupLookups = normalizeResponse(response) || {};
                    callback();
                },
                error: function () {
                    popupLookups = {};
                    callback();
                }
            });
        }

        function loadRunPaymentDetails(run, amountText) {
            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_033_UpcomingAPRunsWidget/GetUpcomingAPRunDetails',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: {
                    runDate: run.runDate || run.dueDate || '',
                    paymentMethodId: run.paymentMethodId || 0
                },
                success: function (response) {
                    var data = normalizeResponse(response);

                    if (!data || data.error) {
                        showPayError((data && (data.errorText || data.error)) || lbl('VAS_ErrorLoading', 'Could not load data'));
                        return;
                    }

                    paymentRows = $.isArray(data.rows) ? data.rows : [];

                    if (paymentRows.length === 0) {
                        showPayError(lbl('VAS_031_MessageNoData', 'No Data'));
                        return;
                    }

                    selectedPaymentRow = paymentRows[0];
                    renderPayDialogGrid(selectedPaymentRow, amountText);
                    updatePayNotice(selectedPaymentRow);
                },
                error: function () {
                    showPayError(lbl('VAS_ErrorLoading', 'Could not load data'));
                },
                complete: function () {
                    setPayDialogBusy(false, false);
                }
            });
        }

        function normalizeResponse(response) {
            var data = response;

            if (typeof data === 'string') {
                try {
                    data = JSON.parse(data);
                }
                catch (e) {
                    data = null;
                }
            }

            if (typeof data === 'string') {
                try {
                    data = JSON.parse(data);
                }
                catch (e2) {
                    data = null;
                }
            }

            return data;
        }

        function renderPayDialogGrid(row, amountText) {
            if (!$payDialogGrid) {
                return;
            }

            var bankText = getBankAccountDisplay(row);
            var currencyText = (row.currencyISO || '') + (row.currencyISO && row.currencySymbol ? ' · ' : '') + (row.currencySymbol || '');
            var html =
                fieldHtml(lbl('VAS_031_MessageOrganization', 'Organization'), selectHtml('adOrgId', getLookup('organizations'), row.organizationId), false, true) +
                fieldHtml(lbl('VAS_031_MessageBankAccount', 'Bank Account'), selectHtml('bankAccountId', getLookup('bankAccounts'), row.bankAccountId, null, null, bankText), true, true) +
                fieldHtml(lbl('VAS_031_MessageTransactionDate', 'Transaction Date'), inputHtml('transactionDate', 'date', row.transactionDate || ''), false, true) +
                fieldHtml(lbl('VAS_032_MessageVendor', 'Vendor'), selectHtml('vendorId', getLookup('vendors'), row.vendorId, null, null, row.vendorName), true, true) +
                fieldHtml(lbl('VAS_PaymentCurrency', 'Currency'), selectHtml('currencyId', getLookup('currencies'), row.cCurrencyId, null, null, currencyText), true, true) +
                fieldHtml(lbl('VAS_031_MessageCurrencyType', 'Currency Type'), selectHtml('conversionTypeId', getLookup('conversionTypes'), row.conversionTypeId, null, null, row.currencyTypeName || lbl('VAS_031_MessageSpot', 'Spot')), false, true) +
                fieldHtml(lbl('VAS_031_MessagePaymentAmount', 'Payment Amount'), inputHtml('payAmt', 'number', normalizeNumber(row.amount), '0.01'), true, true) +
                fieldHtml(lbl('VIS_InvoiceNo', 'Invoice No.'), inputHtml('documentNo', 'text', row.documentNo || ''), true, true);

            $payDialogGrid.html(html);
        }

        function fieldHtml(label, value, prefilled, rawValue) {
            return '<div class="vas-upcoming-ap-runs-field' + (prefilled ? ' is-prefilled' : '') + '">' +
                '<div class="vas-upcoming-ap-runs-field-label">' +
                escapeHtml(label) +
                (prefilled ? '<span>' + escapeHtml(lbl('VAS_031_MessagePrefilled', 'PRE-FILLED')) + '</span>' : '') +
                '</div>' +
                '<div class="vas-upcoming-ap-runs-field-value">' +
                (rawValue ? (value || '') : escapeHtml(value || '')) +
                '</div>' +
                '</div>';
        }

        function getLookup(name) {
            return popupLookups && $.isArray(popupLookups[name]) ? popupLookups[name] : [];
        }

        function selectHtml(fieldName, items, selectedValue, textProp, valueProp, fallbackText) {
            var html = '<select class="vas-upcoming-ap-runs-edit-control" data-pay-field="' + escapeHtml(fieldName) + '">';
            var hasSelected = false;

            items = $.isArray(items) ? items : [];

            for (var i = 0; i < items.length; i++) {
                var item = items[i] || {};
                var value = valueProp ? item[valueProp] : (item.id != null ? item.id : item.paymentId);
                var text = textProp ? item[textProp] : (item.name || item.documentNo || value);
                var selected = String(value) === String(selectedValue);

                if (selected) {
                    hasSelected = true;
                }

                html += '<option value="' + escapeHtml(value) + '"' + (selected ? ' selected' : '') + '>' + escapeHtml(text || value || '') + '</option>';
            }

            if (!hasSelected && selectedValue != null && selectedValue !== '') {
                html += '<option value="' + escapeHtml(selectedValue) + '" selected>' + escapeHtml(fallbackText || selectedValue) + '</option>';
            }

            html += '</select>';
            return html;
        }

        function inputHtml(fieldName, type, value, step, readonly) {
            return '<input class="vas-upcoming-ap-runs-edit-control" data-pay-field="' + escapeHtml(fieldName) + '" type="' + escapeHtml(type) + '" value="' + escapeHtml(value || '') + '"' +
                (step ? ' step="' + escapeHtml(step) + '"' : '') +
                (readonly ? ' readonly' : '') +
                '>';
        }

        function updatePayNotice(row) {
            if (!$payDialogNotice || !row) {
                return;
            }

            var amountText = formatCurrencyAmount(row.amount, row.currencySymbol, row.currencyISO, row.stdPrecision);

            $payDialogNotice.html(
                escapeHtml(lbl('VAS_031_MessagePrefilledForInvoice', 'Pre-filled for invoice')) +
                ' <strong>' + escapeHtml(row.documentNo || '') + '</strong> — ' +
                escapeHtml(row.vendorName || '') + ' · ' +
                escapeHtml(amountText) + '. ' +
                escapeHtml(lbl('VAS_031_MessageReviewAndSave', 'Review and save.'))
            );
        }

        function getBankAccountDisplay(run) {
            var bankName = run.bankName || '';
            var accountName = run.bankAccountName || '';
            var accountNo = run.bankAccountNo || '';
            var tail = accountNo ? String(accountNo).slice(-4) : '';
            var name = bankName || accountName;

            if (name && tail) {
                return name + ' · ****' + tail + (accountName ? ' · ' + accountName : '');
            }

            return accountName || bankName || (tail ? '****' + tail : '');
        }

        function formatShortDate(value) {
            if (!value) {
                return '';
            }

            var date = new Date(value);

            if (isNaN(date.getTime())) {
                return value;
            }

            return date.toLocaleDateString(window.navigator.language, {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit'
            });
        }

        function savePayDialog() {
            if (!selectedRun || !selectedPaymentRow || saveInProgress) {
                return;
            }

            var payload = readPayDialogPayload();

            if (!payload) {
                return;
            }

            saveInProgress = true;
            setPayDialogBusy(true, true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_033_UpcomingAPRunsWidget/CreateUpcomingAPPayment',
                type: 'POST',
                dataType: 'json',
                data: payload,
                success: function (response) {
                    var data = response;

                    if (typeof response === 'string') {
                        try {
                            data = JSON.parse(response);
                        }
                        catch (e) {
                            data = null;
                        }
                    }

                    if (!data || data.success === false || data.error) {
                        showPayError((data && (data.error || data.message)) || lbl('VAS_ErrorLoading', 'Could not save data'));
                        return;
                    }

                    saveInProgress = false;
                    setPayDialogBusy(false, true);
                    closePayDialog();
                    loadData();
                },
                error: function () {
                    showPayError(lbl('VAS_ErrorLoading', 'Could not save data'));
                },
                complete: function () {
                    if (saveInProgress) {
                        saveInProgress = false;
                        setPayDialogBusy(false, true);
                    }
                }
            });
        }

        function readPayDialogPayload() {
            var $field = $payDialogGrid ? $payDialogGrid.find('[data-pay-field]') : $();
            var payload = {};

            $field.each(function () {
                payload[$(this).attr('data-pay-field')] = $(this).val();
            });

            payload.paymentId = Number(payload.paymentId || selectedPaymentRow.paymentId || 0);
            payload.adOrgId = Number(payload.adOrgId || selectedPaymentRow.organizationId || 0);
            payload.bankAccountId = Number(payload.bankAccountId || selectedPaymentRow.bankAccountId || 0);
            payload.vendorId = Number(payload.vendorId || selectedPaymentRow.vendorId || 0);
            payload.currencyId = Number(payload.currencyId || selectedPaymentRow.cCurrencyId || 0);
            payload.conversionTypeId = Number(payload.conversionTypeId || selectedPaymentRow.conversionTypeId || 0);
            payload.transactionDate = payload.transactionDate || selectedPaymentRow.transactionDate || '';
            payload.payAmt = Number(payload.payAmt || 0);

            if (payload.paymentId <= 0 || payload.payAmt <= 0 || !payload.transactionDate) {
                showPayError(lbl('VAS_031_MessageReviewRequiredFields', 'Review required fields before saving.'));
                return null;
            }

            return payload;
        }

        function showPayError(message) {
            if ($payDialogNotice) {
                $payDialogNotice
                    .addClass('vas-upcoming-ap-runs-pay-error')
                    .text(message || lbl('VAS_ErrorLoading', 'Could not save data'));
            }
        }

        function setPayDialogBusy(show, isSaving) {
            var busy = !!show;
            var saving = !!isSaving && busy;

            if ($payDialogBusy) {
                $payDialogBusy
                    .text(saving ? lbl('VAS_031_MessageSaving', 'Saving') : lbl('VAS_031_MessageLoadingDetails', 'Loading payment details'))
                    .toggleClass('is-visible', busy);
            }

            if ($payDialogSave) {
                $payDialogSave
                    .prop('disabled', busy)
                    .toggleClass('is-loading', saving);
            }

            if ($payDialogSaveLabel) {
                $payDialogSaveLabel.text(saving
                    ? lbl('VAS_031_MessageSaving', 'Saving')
                    : lbl('VAS_031_MessageSavePayment', 'Save payment'));
            }

            if ($payDialogNotice) {
                $payDialogNotice.removeClass('vas-upcoming-ap-runs-pay-error');
            }
        }

        function createPayDialog() {
            if ($payDialog) {
                return;
            }

            $payDialog = $(
                '<div class="vas-upcoming-ap-runs-pay-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-upcoming-ap-runs-pay-scrim"></div>' +
                '<div class="vas-upcoming-ap-runs-pay-card">' +
                '<div class="vas-upcoming-ap-runs-pay-header">' +
                '<span class="vas-upcoming-ap-runs-pay-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<rect x="3" y="5" width="18" height="14" rx="2"></rect><line x1="3" y1="10" x2="21" y2="10"></line>' +
                '</svg>' +
                '</span>' +
                '<div class="vas-upcoming-ap-runs-pay-title-group">' +
                '<div class="vas-upcoming-ap-runs-pay-title"></div>' +
                '<div class="vas-upcoming-ap-runs-pay-sub"></div>' +
                '</div>' +
                '<button type="button" class="vas-upcoming-ap-runs-pay-close" aria-label="' + escapeHtml(lbl('VAS_Close', 'Close')) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<line x1="18" y1="6" x2="6" y2="18"></line>' +
                '<line x1="6" y1="6" x2="18" y2="18"></line>' +
                '</svg>' +
                '</button>' +
                '</div>' +
                '<div class="vas-upcoming-ap-runs-pay-body">' +
                '<div class="vas-upcoming-ap-runs-pay-busy">' + escapeHtml(lbl('VAS_031_MessageSaving', 'Saving')) + '</div>' +
                '<div class="vas-upcoming-ap-runs-pay-notice"></div>' +
                '<div class="vas-upcoming-ap-runs-pay-grid"></div>' +
                '</div>' +
                '<div class="vas-upcoming-ap-runs-pay-footer">' +
                '<span>' + escapeHtml(lbl('VAS_031_MessageGeneratedFromUpcoming', 'Generated from Upcoming · 7 days')) + '</span>' +
                '<div class="vas-upcoming-ap-runs-pay-footer-actions">' +
                '<button type="button" class="vas-upcoming-ap-runs-pay-cancel">' + escapeHtml(lbl('VAS_Cancel', 'Cancel')) + '</button>' +
                '<button type="button" class="vas-upcoming-ap-runs-pay-save">' +
                '<span class="vas-upcoming-ap-runs-save-spinner" aria-hidden="true"></span>' +
                '<span class="vas-upcoming-ap-runs-save-check" aria-hidden="true">✓</span>' +
                '<span class="vas-upcoming-ap-runs-save-label">' + escapeHtml(lbl('VAS_031_MessageSavePayment', 'Save payment')) + '</span>' +
                '</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $payDialogTitle = $payDialog.find('.vas-upcoming-ap-runs-pay-title');
            $payDialogSub = $payDialog.find('.vas-upcoming-ap-runs-pay-sub');
            $payDialogNotice = $payDialog.find('.vas-upcoming-ap-runs-pay-notice');
            $payDialogGrid = $payDialog.find('.vas-upcoming-ap-runs-pay-grid');
            $payDialogSave = $payDialog.find('.vas-upcoming-ap-runs-pay-save');
            $payDialogSaveLabel = $payDialog.find('.vas-upcoming-ap-runs-save-label');
            $payDialogBusy = $payDialog.find('.vas-upcoming-ap-runs-pay-busy');

            $payDialog.find('.vas-upcoming-ap-runs-pay-close, .vas-upcoming-ap-runs-pay-cancel').on('click', function () {
                closePayDialog();
            });

            $payDialog.find('.vas-upcoming-ap-runs-pay-scrim').on('click', function () {
                closePayDialog();
            });

            $payDialogSave.on('click', function () {
                savePayDialog();
            });

            $(document).on('keydown.vas-upcoming-ap-runs-pay', function (e) {
                if (e.key === 'Escape' && $payDialog && $payDialog.is(':visible')) {
                    closePayDialog();
                }
            });

            $('body').append($payDialog);
        }

        function pickRunValue(run, camelName, fallbackName) {
            if (!run) {
                return null;
            }

            if (run[camelName] !== undefined && run[camelName] !== null && run[camelName] !== '') {
                return run[camelName];
            }

            if (run[fallbackName] !== undefined && run[fallbackName] !== null && run[fallbackName] !== '') {
                return run[fallbackName];
            }

            return null;
        }

        function getRunTitle(paymentMethodName, vendorName, paymentCount) {
            if (paymentCount === 1 && vendorName) {
                return paymentMethodName + ' · ' + vendorName;
            }

            return paymentMethodName;
        }

        function getPaymentLabel(paymentCount) {
            return paymentCount === 1
                ? lbl('VAS_031_MessagePayment', 'payment')
                : lbl('VAS_031_MessagePayments', 'payments');
        }

        function getPaymentMethodClass(paymentMethodName) {
            var method = (paymentMethodName || '').toLowerCase();

            if (method.indexOf('rtgs') >= 0) {
                return 'vas-upcoming-ap-runs-bar-rtgs';
            }

            if (method.indexOf('upi') >= 0) {
                return 'vas-upcoming-ap-runs-bar-upi';
            }

            if (method.indexOf('card') >= 0) {
                return 'vas-upcoming-ap-runs-bar-card';
            }

            if (method.indexOf('cheque') >= 0 || method.indexOf('check') >= 0) {
                return 'vas-upcoming-ap-runs-bar-cheque';
            }

            return 'vas-upcoming-ap-runs-bar-neft';
        }

        function formatDate(value) {
            if (!value) {
                return '';
            }

            var date = new Date(value);

            if (typeof value === 'string' && value.indexOf('/Date(') === 0) {
                var timestamp = Number(value.replace(/[^0-9-]/g, ''));
                date = new Date(timestamp);
            }

            if (isNaN(date.getTime())) {
                return value;
            }

            return date.toLocaleDateString(window.navigator.language, {
                weekday: 'short',
                day: '2-digit',
                month: 'short'
            });
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO, stdPrecision) {
            var numericValue = Number(value || 0);
            var precision = Number(stdPrecision);

            if (isNaN(precision) && VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                precision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            if (isNaN(precision) || precision < 0) {
                precision = 2;
            }

            var amount = numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });

            if (currencySymbol) {
                return currencySymbol + amount;
            }

            return currencyISO ? amount + ' ' + currencyISO : amount;
        }

        function normalizeNumber(value) {
            var numericValue = Number(value || 0);

            if (isNaN(numericValue)) {
                return '0';
            }

            return String(numericValue);
        }

        function showBusy(show) {
            if ($busy) {
                $busy.toggleClass('is-visible', !!show);
            }
        }

        function showState(show, message) {
            if ($state) {
                $state.text(message || '').toggleClass('is-visible', !!show);
            }

            if ($body) {
                $body.toggle(!show);
            }
        }

        function setNoData() {
            totalPages = 0;
            pageNo = 1;

            updatePager();
            showState(true, lbl('VAS_031_MessageNoData', 'No Data'));
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;
            $(document).off('keydown.vas-upcoming-ap-runs-pay');
            $('body').removeClass('vas-upcoming-ap-runs-body-lock');
            if ($payDialog) {
                $payDialog.remove();
            }
            $root.remove();
            $card = null;
            $body = null;
            $pager = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;
            $busy = null;
            $state = null;
            $payDialog = null;
            $payDialogTitle = null;
            $payDialogSub = null;
            $payDialogNotice = null;
            $payDialogGrid = null;
            $payDialogSave = null;
            $payDialogSaveLabel = null;
            $payDialogBusy = null;
        };
    };

    VAS.VAS_031_UpcomingAPRunsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_031_UpcomingAPRunsWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_031_UpcomingAPRunsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_031_UpcomingAPRunsWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
