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
        var $payDialogBusy;

        var isDisposed = false;
        var runsData = [];
        var selectedRun = null;
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

            var vendorName = run.vendorName || lbl('VAS_031_MessageNotSpecified', 'Not Specified');
            var documentNo = run.documentNo || '';
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
                $payDialogSub.text(lbl('VAS_031_MessagePrefilledFromUpcoming', 'Pre-filled from upcoming') + ' · ' + vendorName);
            }

            if ($payDialogNotice) {
                $payDialogNotice.html(
                    escapeHtml(lbl('VAS_031_MessagePrefilledForInvoice', 'Pre-filled for invoice')) +
                    ' <strong>' + escapeHtml(documentNo) + '</strong> — ' +
                    escapeHtml(vendorName) + ' · ' +
                    escapeHtml(amountText) + '. ' +
                    escapeHtml(lbl('VAS_031_MessageReviewAndSave', 'Review and save.'))
                );
            }

            renderPayDialogGrid(run, amountText);
            setPayDialogBusy(false);
            $payDialog.show();
            $('body').addClass('vas-upcoming-ap-runs-body-lock');
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

        function renderPayDialogGrid(run, amountText) {
            if (!$payDialogGrid) {
                return;
            }

            var bankText = getBankAccountDisplay(run);
            var currencyText = (run.currencyISO || '') + (run.currencyISO && run.currencySymbol ? ' · ' : '') + (run.currencySymbol || '');

            var html =
                fieldHtml(lbl('VAS_031_MessageOrganization', 'Organization'), run.organizationName || '') +
                fieldHtml(lbl('VAS_031_MessageBankAccount', 'Bank Account'), bankText, true) +
                fieldHtml(lbl('VAS_031_MessageTransactionDate', 'Transaction Date'), formatShortDate(run.dueDate || run.runDate)) +
                fieldHtml(lbl('VAS_032_MessageVendor', 'Vendor'), run.vendorName || '', true) +
                fieldHtml(lbl('VAS_PaymentCurrency', 'Currency'), currencyText, true) +
                fieldHtml(lbl('VAS_031_MessageCurrencyType', 'Currency Type'), lbl('VAS_031_MessageSpot', 'Spot')) +
                fieldHtml(lbl('VAS_031_MessagePaymentAmount', 'Payment Amount'), amountText, true) +
                fieldHtml(lbl('VIS_InvoiceNo', 'Invoice No.'), run.documentNo || '', true);

            $payDialogGrid.html(html);
        }

        function fieldHtml(label, value, prefilled) {
            return '<div class="vas-upcoming-ap-runs-field">' +
                '<div class="vas-upcoming-ap-runs-field-label">' +
                escapeHtml(label) +
                (prefilled ? '<span>' + escapeHtml(lbl('VAS_031_MessagePrefilled', 'PRE-FILLED')) + '</span>' : '') +
                '</div>' +
                '<div class="vas-upcoming-ap-runs-field-value" title="' + escapeHtml(value || '') + '">' +
                escapeHtml(value || '') +
                '</div>' +
                '</div>';
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
            if (!selectedRun || saveInProgress) {
                return;
            }

            saveInProgress = true;
            setPayDialogBusy(true);

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_033_UpcomingAPRunsWidget/CreateUpcomingAPPayment',
                type: 'POST',
                dataType: 'json',
                data: {
                    invoiceId: selectedRun.invoiceId || 0,
                    invoicePayScheduleId: selectedRun.invoicePayScheduleId || 0,
                    bankAccountId: selectedRun.bankAccountId || 0,
                    payAmt: Number(pickRunValue(selectedRun, 'totalAmount', 'amount') || 0)
                },
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
                    setPayDialogBusy(false);
                    closePayDialog();
                    loadData();
                },
                error: function () {
                    showPayError(lbl('VAS_ErrorLoading', 'Could not save data'));
                },
                complete: function () {
                    if (saveInProgress) {
                        saveInProgress = false;
                        setPayDialogBusy(false);
                    }
                }
            });
        }

        function showPayError(message) {
            if ($payDialogNotice) {
                $payDialogNotice
                    .addClass('vas-upcoming-ap-runs-pay-error')
                    .text(message || lbl('VAS_ErrorLoading', 'Could not save data'));
            }
        }

        function setPayDialogBusy(show) {
            saveInProgress = !!show;

            if ($payDialogBusy) {
                $payDialogBusy.toggleClass('is-visible', !!show);
            }

            if ($payDialogSave) {
                $payDialogSave.prop('disabled', !!show);
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
                '<button type="button" class="vas-upcoming-ap-runs-pay-close" aria-label="' + escapeHtml(lbl('VAS_Close', 'Close')) + '">×</button>' +
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
                '<button type="button" class="vas-upcoming-ap-runs-pay-save">✓ ' + escapeHtml(lbl('VAS_031_MessageSavePayment', 'Save payment')) + '</button>' +
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
