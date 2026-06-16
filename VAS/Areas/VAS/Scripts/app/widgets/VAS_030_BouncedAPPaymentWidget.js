/**
 * Bounced
 * Purpose - Shows outgoing AP payments that were reversed/bounced and need re-issue.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Bounced                              | VAS_030_MessageBounced
 *  2  | Need re-issue                        | VAS_030_MessageNeedReissue
 *  3  | Loading                              | VAS_030_MessageLoading
 *  4  | No Data                              | VAS_030_MessageNoData
 * ─────────────────────────────────────────────────────────────────────
 */


; VAS = window.VAS || {};

; (function (VAS, $) {
    "use strict";

    VAS.VAS_030_BouncedAPPaymentWidget = function () {
        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;
        var self = this;

        var $root = $('<div class="vas-bounced-ap-payment-root">');
        var $card = null;
        var $value = null;
        var $description = null;
        var $body = null;
        var $footer = null;
        var $busy = null;
        var $state = null;
        var $dialog = null;
        var $dialogTbody = null;
        var $dialogBusy = null;
        var $pagerHelper = null;
        var $pagerPrev = null;
        var $pagerNext = null;
        var $pagerText = null;
        var pageNo = 1;
        var pageSize = 10;
        var totalPages = 0;
        var totalRecords = 0;
        var rowsLoading = false;
        var isDisposed = false;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            $card = $('<div class="vas-bounced-ap-payment-card">');
            $card.attr({ role: 'button', tabindex: '0' });

            var $header = $('<div class="vas-bounced-ap-payment-header">');
            var $iconBox = $('<div class="vas-bounced-ap-payment-icon-box">');

            var $icon = $(
                '<svg class="vas-bounced-ap-payment-icon" viewBox="0 0 24 24" fill="none" aria-hidden="true" focusable="false">' +
                '<path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"></path>' +
                '<line x1="4" y1="22" x2="4" y2="15"></line>' +
                '</svg>'
            );

            var $title = $('<div class="vas-bounced-ap-payment-title">').text(
                lbl('VAS_030_MessageBounced', 'Bounced')
            );

            $iconBox.append($icon);
            $header.append($iconBox).append($title);

            $body = $('<div class="vas-bounced-ap-payment-body">');
            $value = $('<div class="vas-bounced-ap-payment-value">');
            $body.append($value);

            $footer = $('<div class="vas-bounced-ap-payment-footer">');

     
            $description = $('<div class="vas-bounced-ap-payment-desc">').text(
                lbl('VAS_030_MessageNeedReissue', 'Need re-issue')
            );

            $footer.append($description);
            $busy = $('<div class="vas-bounced-ap-payment-busy">').text(lbl('VAS_030_MessageLoading', 'Loading'));
            $state = $('<div class="vas-bounced-ap-payment-state-message">');

            $card.append($header).append($body).append($footer).append($busy).append($state);
            $root.empty().append($card);

            $card.on('click', openDialog);
            $card.on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });

            createDialog();
        }

        function loadData() {
            if (isDisposed) {
                return;
            }

            showBusy(true);
            showState(false, '');

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_030_BouncedAPPaymentWidget/GetBouncedAPPayments',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = normalizeResponse(response);

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

        function normalizeResponse(response) {
            if (typeof response !== 'string') {
                return response;
            }

            try {
                return JSON.parse(response);
            }
            catch (e) {
                return null;
            }
        }

        function escapeHtml(value) {
            return String(value == null ? '' : value)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#039;');
        }

        function renderData(data) {
            var count = Number(data.value);

            if (isNaN(count)) {
                count = Number(data.bouncedPaymentCount);
            }

            if (isNaN(count) || count <= 0) {
                setNoData();
                return;
            }

            showState(false, '');
            $value.text(formatCount(count));

            if ($description && data.description) {
                $description.text(data.description);
            }
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: 0,
                maximumFractionDigits: 0
            });
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

            if ($footer) {
                $footer.toggle(!show);
            }
        }

        function setNoData() {
            showState(true, lbl('VAS_030_MessageNoData', 'No Data'));
        }

        function openDialog() {
            if (!$dialog) {
                return;
            }

            $dialog.show();
            $('body').addClass('vas-bounced-ap-payment-body-lock');
            pageNo = 1;
            loadRows();
        }

        function closeDialog() {
            if (!$dialog) {
                return;
            }

            $dialog.hide();
            $('body').removeClass('vas-bounced-ap-payment-body-lock');
            pageNo = 1;
        }

        function loadRows() {
            if (!$dialogTbody || rowsLoading) {
                return;
            }

            rowsLoading = true;
            showDialogBusy(true);
            updatePager();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_030_BouncedAPPaymentWidget/GetBouncedAPPaymentRows',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: {
                    pageNo: pageNo,
                    pageSize: pageSize
                },
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    var data = normalizeResponse(response);

                    if (!data || data.error) {
                        renderRows([]);
                        totalRecords = 0;
                        totalPages = 0;
                        updatePager();
                        return;
                    }

                    totalRecords = Number(data.totalRecords || 0);
                    totalPages = Number(data.totalPages || 0);

                    if (typeof data.pageNo !== 'undefined') {
                        pageNo = Number(data.pageNo);
                    }

                    if (pageNo > totalPages && totalPages > 0) {
                        pageNo = totalPages;
                    }

                    if (pageNo < 1) {
                        pageNo = 1;
                    }

                    renderRows(data.rows || []);
                    updatePager();
                },
                error: function () {
                    if (!isDisposed) {
                        renderRows([]);
                        totalRecords = 0;
                        totalPages = 0;
                        updatePager();
                    }
                },
                complete: function () {
                    rowsLoading = false;
                    showDialogBusy(false);
                    updatePager();
                }
            });
        }

        function renderRows(rows) {
            if (!$dialogTbody) {
                return;
            }

            $dialogTbody.empty();

            if (!rows || rows.length === 0) {
                $dialogTbody.html(
                    '<tr><td class="vas-bounced-ap-payment-dialog-empty" colspan="8">' +
                    escapeHtml(lbl('VAS_030_MessageNoData', 'No Data')) +
                    '</td></tr>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var dateText = formatDate(row.paymentDate);
                var bankText = formatBank(row);
                var symbol = row.currencySymbol || row.currency || '';
                var amountText = formatAmount(row.amount);
                var amountHtml = (symbol ? '<span class="vas-bounced-ap-payment-currency-inline">' + escapeHtml(symbol) + '</span>' : '') + escapeHtml(amountText);

                $dialogTbody.append(
                    '<tr>' +
                    '<td class="vas-bounced-ap-payment-td-doc" title="' + escapeHtml(row.paymentNo || '') + '">' + escapeHtml(row.paymentNo || '') + '</td>' +
                    '<td class="vas-bounced-ap-payment-td-date" title="' + escapeHtml(dateText) + '">' + escapeHtml(dateText) + '</td>' +
                    '<td class="vas-bounced-ap-payment-td-vendor" title="' + escapeHtml(row.vendorName || '') + '">' + escapeHtml(row.vendorName || '') + '</td>' +
                    '<td class="vas-bounced-ap-payment-td-bank" title="' + escapeHtml(bankText) + '">' + escapeHtml(bankText) + '</td>' +
                    '<td class="vas-bounced-ap-payment-td-currency" title="' + escapeHtml(row.currency || '') + '">' + escapeHtml(row.currency || '') + '</td>' +
                    '<td class="vas-bounced-ap-payment-td-amount" title="' + escapeHtml((symbol ? symbol + ' ' : '') + amountText) + '">' + amountHtml + '</td>' +
                    '<td class="vas-bounced-ap-payment-td-method" title="' + escapeHtml(row.method || '') + '">' + escapeHtml(row.method || '') + '</td>' +
                    '<td class="vas-bounced-ap-payment-td-status"><span class="vas-bounced-ap-payment-status-chip">' + escapeHtml(row.status || '') + '</span></td>' +
                    '</tr>'
                );
            }
        }

        function updatePager() {
            if ($pagerHelper) {
                if (totalRecords > 0) {
                    var from = (pageNo - 1) * pageSize + 1;
                    var to = Math.min((pageNo - 1) * pageSize + pageSize, totalRecords);

                    $pagerHelper.text(
                        lbl('VAS_Showing', 'Showing') + ' ' +
                        from + '-' + to + ' ' +
                        lbl('VAS_Of', 'of') + ' ' +
                        totalRecords
                    );
                }
                else {
                    $pagerHelper.text('');
                }
            }

            if ($pagerText) {
                $pagerText.text(totalPages > 0 ? (pageNo + ' ' + lbl('VAS_Of', 'of') + ' ' + totalPages) : '');
            }

            if ($pagerPrev) {
                $pagerPrev.prop('disabled', rowsLoading || pageNo <= 1);
            }

            if ($pagerNext) {
                $pagerNext.prop('disabled', rowsLoading || totalPages <= 1 || pageNo >= totalPages);
            }
        }

        function showDialogBusy(show) {
            if ($dialogBusy) {
                $dialogBusy.toggleClass('is-visible', !!show);
            }
        }

        function createDialog() {
            $dialog = $(
                '<div class="vas-bounced-ap-payment-dialog" style="display:none;" role="dialog" aria-modal="true">' +
                '<div class="vas-bounced-ap-payment-dialog-scrim"></div>' +
                '<div class="vas-bounced-ap-payment-dialog-card">' +
                '<div class="vas-bounced-ap-payment-dialog-header">' +
                '<div class="vas-bounced-ap-payment-dialog-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="13"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>' +
                '</div>' +
                '<div class="vas-bounced-ap-payment-dialog-title-group">' +
                '<div class="vas-bounced-ap-payment-dialog-title">' + escapeHtml(lbl('VAS_030_MessageBouncedAPPayments', 'Bounced AP payments')) + '</div>' +
                '<div class="vas-bounced-ap-payment-dialog-subtitle">' + escapeHtml(lbl('VAS_030_MessageNeedReissue', 'Need re-issue')) + '</div>' +
                '</div>' +
                '<button type="button" class="vas-bounced-ap-payment-dialog-close" aria-label="' + escapeHtml(lbl('VAS_Close', 'Close')) + '"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>' +
                '</div>' +
                '<div class="vas-bounced-ap-payment-dialog-body">' +
                '<div class="vas-bounced-ap-payment-dialog-busy">' + escapeHtml(lbl('VAS_030_MessageLoading', 'Loading')) + '</div>' +
                '<table class="vas-bounced-ap-payment-dialog-table">' +
                '<thead><tr>' +
                '<th>' + escapeHtml(lbl('VAS_030_MessagePaymentNo', 'Payment No.')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_Date', 'Date')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_Vendor', 'Vendor')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_BankAccount', 'Bank account')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_PaymentCurrency', 'Payment Currency')) + '</th>' +
                '<th class="vas-bounced-ap-payment-th-amount">' + escapeHtml(lbl('VAS_Amount', 'Amount')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_Method', 'Method')) + '</th>' +
                '<th>' + escapeHtml(lbl('VAS_Status', 'Status')) + '</th>' +
                '</tr></thead>' +
                '<tbody class="vas-bounced-ap-payment-dialog-tbody"></tbody>' +
                '</table>' +
                '</div>' +
                '<div class="vas-bounced-ap-payment-dialog-footer">' +
                '<span class="vas-bounced-ap-payment-pager-helper"></span>' +
                '<div class="vas-bounced-ap-payment-pager">' +
                '<button type="button" class="vas-bounced-ap-payment-pager-btn vas-bounced-ap-payment-pager-prev" aria-label="' + escapeHtml(lbl('VAS_Previous', 'Previous')) + '">‹</button>' +
                '<span class="vas-bounced-ap-payment-pager-text"></span>' +
                '<button type="button" class="vas-bounced-ap-payment-pager-btn vas-bounced-ap-payment-pager-next" aria-label="' + escapeHtml(lbl('VAS_Next', 'Next')) + '">›</button>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '</div>'
            );

            $dialogTbody = $dialog.find('.vas-bounced-ap-payment-dialog-tbody');
            $dialogBusy = $dialog.find('.vas-bounced-ap-payment-dialog-busy');
            $pagerHelper = $dialog.find('.vas-bounced-ap-payment-pager-helper');
            $pagerPrev = $dialog.find('.vas-bounced-ap-payment-pager-prev');
            $pagerNext = $dialog.find('.vas-bounced-ap-payment-pager-next');
            $pagerText = $dialog.find('.vas-bounced-ap-payment-pager-text');

            $dialog.find('.vas-bounced-ap-payment-dialog-close').on('click', closeDialog);
            $dialog.find('.vas-bounced-ap-payment-dialog-scrim').on('click', closeDialog);

            $pagerPrev.on('click', function () {
                if (rowsLoading || pageNo <= 1) {
                    return;
                }

                pageNo--;
                loadRows();
            });

            $pagerNext.on('click', function () {
                if (rowsLoading || pageNo >= totalPages) {
                    return;
                }

                pageNo++;
                loadRows();
            });

            $(document).on('keydown.vas-bounced-ap-payment-' + self.AD_UserHomeWidgetID, function (e) {
                if (e.key === 'Escape' && $dialog.is(':visible')) {
                    closeDialog();
                }
            });

            $('body').append($dialog);
        }

        function formatDate(value) {
            if (!value) {
                return '';
            }

            var date = new Date(value);

            if (isNaN(date.getTime())) {
                return value;
            }

            return date.toLocaleDateString(window.navigator.language, {
                day: '2-digit',
                month: 'short',
                year: 'numeric'
            });
        }

        function formatAmount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: getStdPrecision(),
                maximumFractionDigits: getStdPrecision()
            });
        }

        function getStdPrecision() {
            var stdPrecision = 2;

            try {
                if (VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                    stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
                }
            }
            catch (e) {
                stdPrecision = 2;
            }

            return isNaN(stdPrecision) || stdPrecision < 0 ? 2 : stdPrecision;
        }

        function formatBank(row) {
            var bankName = row && row.bankName ? String(row.bankName).trim() : '';
            var accountNo = row && row.accountNo ? String(row.accountNo).trim() : '';
            var last4 = accountNo ? (accountNo.length > 4 ? accountNo.slice(-4) : accountNo) : '';

            if (bankName && last4) {
                return bankName + ' - ****' + last4;
            }

            if (bankName) {
                return bankName;
            }

            if (last4) {
                return '****' + last4;
            }

            return '';
        }

        this.refreshData = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;
            $(document).off('keydown.vas-bounced-ap-payment-' + this.AD_UserHomeWidgetID);
            $('body').removeClass('vas-bounced-ap-payment-body-lock');

            if ($dialog) {
                $dialog.remove();
            }

            $root.remove();

            $card = null;
            $value = null;
            $description = null;
            $body = null;
            $footer = null;
            $busy = null;
            $state = null;
            $dialog = null;
            $dialogTbody = null;
            $dialogBusy = null;
            $pagerHelper = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;
        };
    };

    VAS.VAS_030_BouncedAPPaymentWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;

        if (frame && frame.widgetInfo) {
            this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        }

        this.Initalize();

        if (this.frame && this.frame.getContentGrid) {
            this.frame.getContentGrid().append(this.getRoot());
        }
    };

    VAS.VAS_030_BouncedAPPaymentWidget.prototype.widgetSizeChange = function (height, width) {
        var $root = this.getRoot();

        if (!$root) {
            return;
        }

        $root.toggleClass(
            'vas-bounced-ap-payment-compact',
            (width && width < 240) || (height && height < 160)
        );
    };

    VAS.VAS_030_BouncedAPPaymentWidget.prototype.refreshWidget = function () {
        this.refreshData();
    };

    VAS.VAS_030_BouncedAPPaymentWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame && this.frame.dispose) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
