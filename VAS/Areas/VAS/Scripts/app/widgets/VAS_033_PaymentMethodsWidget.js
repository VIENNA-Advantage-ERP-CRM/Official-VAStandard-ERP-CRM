/**
 * Payment methods
 * Purpose - Shows the distribution of outgoing AP payments by payment method.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Payment methods                      | VAS_033_MessagePaymentMethods
 *  2  | Upi is cheapest - shift small payments where possible | VAS_033_MessagePaymentMethodWhy
 *  3  | Loading                              | VAS_033_MessageLoading
 *  4  | No Data                              | VAS_033_MessageNoData
 *  5  | Not Specified                        | VAS_033_MessageNotSpecified
 *  6  | Previous                             | VAS_Previous
 *  7  | Next                                 | VAS_Next
 *  8  | Could not load data                  | VAS_ErrorLoading
 *  9  | Of                                   | VAS_Of
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_033_PaymentMethodsWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $root = $('<div class="vas-payment-methods-root">');
        var $card;
        var $body;
        var $foot;
        var $pager;
        var $pagerPrev;
        var $pagerNext;
        var $pagerText;
        var $busy;
        var $state;
        var isDisposed = false;
        var methodsData = [];
        var pageNo = 1;
        var pageSize = 4;
        var totalPages = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            $card = $('<div class="vas-payment-methods-card">');

            var $head = $('<div class="vas-payment-methods-head">');
            var $iconBox = $('<span class="vas-payment-methods-icon-box">');
            var $icon = $(
                '<svg class="vas-payment-methods-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<path d="M21.21 15.89A10 10 0 1 1 8 2.83"></path>' +
                '<path d="M22 12A10 10 0 0 0 12 2v10z"></path>' +
                '</svg>'
            );
            var $title = $('<div class="vas-payment-methods-title">').text(lbl('VAS_033_MessagePaymentMethods', 'Payment methods'));

            $pager = $('<div class="vas-payment-methods-pager">');
            $pagerPrev = $('<button type="button" class="vas-payment-methods-page-btn" aria-label="' + lbl('VAS_Previous', 'Previous') + '">‹</button>');
            $pagerText = $('<span class="vas-payment-methods-page-text">');
            $pagerNext = $('<button type="button" class="vas-payment-methods-page-btn" aria-label="' + lbl('VAS_Next', 'Next') + '">›</button>');

            $pager.append($pagerPrev).append($pagerText).append($pagerNext);

            $iconBox.append($icon);
            $head.append($iconBox).append($title);

            $body = $('<div class="vas-payment-methods-body">');

            $foot = $('<div class="vas-payment-methods-foot">');
          
            var $whyText = $('<span class="vas-payment-methods-foot-text">').text(lbl('VAS_033_MessagePaymentMethodWhy', 'Upi is cheapest - shift small payments where possible'));

            $foot.append($whyText).append($pager);
            $busy = $('<div class="vas-payment-methods-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $state = $('<div class="vas-payment-methods-state-message">');

            $card.append($head).append($body).append($foot).append($busy).append($state);
            $root.empty().append($card);

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
                url: VIS.Application.contextUrl + 'VAS_033_PaymentMethodsWidget/GetPaymentMethods',
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
            methodsData = $.isArray(data.methods)
                ? $.grep(data.methods, function (method) {
                    return method != null;
                })
                : [];

            if (methodsData.length === 0) {
                setNoData();
                return;
            }

            pageNo = 1;
            totalPages = Math.ceil(methodsData.length / pageSize);
            renderPage();
        }

        function renderPage() {
            if (!methodsData || methodsData.length === 0) {
                setNoData();
                return;
            }

            totalPages = Math.max(1, Math.ceil(methodsData.length / pageSize));

            if (pageNo < 1) {
                pageNo = 1;
            }

            if (pageNo > totalPages) {
                pageNo = totalPages;
            }

            showState(false, '');
            $body.empty();
            $foot.show();

            var startIndex = (pageNo - 1) * pageSize;
            var pageItems = methodsData.slice(startIndex, startIndex + pageSize);

            for (var i = 0; i < pageItems.length; i++) {
                $body.append(createMethodRow(pageItems[i]));
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

        function createMethodRow(method) {
            var methodName = method.paymentMethodName || lbl('VAS_033_MessageNotSpecified', 'Not Specified');
            var percentage = Number(method.percentage || 0);
            var amountText = formatCurrencyAmount(
                method.paymentAmount,
                method.currencySymbol || method.symbol,
                method.currencyISO,
                method.stdPrecision
            );

            if (isNaN(percentage)) {
                percentage = 0;
            }

            if (percentage > 100) {
                percentage = 100;
            }

            var $row = $('<div class="vas-payment-methods-row">');
            var $top = $('<div class="vas-payment-methods-row-top">');
            var $name = $('<span class="vas-payment-methods-name">').text(methodName);
            var $metrics = $('<span class="vas-payment-methods-metrics">');
            var percentText = formatPercentage(percentage);
            var $percent = $('<span class="vas-payment-methods-percent">').text(percentText);
            var $amount = $('<span class="vas-payment-methods-amount">').text(amountText);
            var tooltipText = methodName + ' | ' + percentText + ' | ' + amountText;
            var $track = $('<div class="vas-payment-methods-track">').attr({
                role: 'progressbar',
                tabindex: '0',
                'aria-label': tooltipText,
                'aria-valuemin': 0,
                'aria-valuemax': 100,
                'aria-valuenow': percentage
            });
            var $fill = $('<div class="vas-payment-methods-fill">').addClass(getMethodClass(methodName));
            var $tooltip = $('<div class="vas-payment-methods-tooltip">').text(tooltipText);

            $fill.css('width', percentage + '%');

            $metrics.append($percent).append($amount);
            $top.append($name).append($metrics);
            $track.append($fill);
            $row.append($top).append($track).append($tooltip);

            return $row;
        }

        function getMethodClass(methodName) {
            var name = (methodName || '').toLowerCase();

            if (name.indexOf('rtgs') >= 0) {
                return 'vas-payment-methods-fill-rtgs';
            }

            if (name.indexOf('upi') >= 0) {
                return 'vas-payment-methods-fill-upi';
            }

            if (name.indexOf('card') >= 0) {
                return 'vas-payment-methods-fill-card';
            }

            if (name.indexOf('cheque') >= 0 || name.indexOf('check') >= 0) {
                return 'vas-payment-methods-fill-cheque';
            }

            return 'vas-payment-methods-fill-neft';
        }

        function formatPercentage(value) {
            var numericValue = Number(value || 0);
            var stdPrecision = getStdPrecision();

            return numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            }) + '%';
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO, precision) {
            var numericValue = Number(value || 0);
            var stdPrecision = normalizePrecision(precision);
            var sign = numericValue < 0 ? '-' : '';
            var absValue = Math.abs(numericValue);

            var amount = absValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });

            if (currencySymbol) {
                return sign + currencySymbol + amount;
            }

            return currencyISO ? sign + amount + ' ' + currencyISO : sign + amount;
        }

        function getStdPrecision() {
            var stdPrecision = 2;

            if (VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                stdPrecision = 2;
            }

            return stdPrecision;
        }

        function normalizePrecision(precision) {
            var stdPrecision = Number(precision);

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                stdPrecision = getStdPrecision();
            }

            return Math.min(stdPrecision, 2);
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

            if ($foot) {
                $foot.toggle(!show);
            }

            if ($body) {
                $body.toggle(!show);
            }

            if ($pager) {
                $pager.toggle(!show);
            }
        }

        function setNoData() {
            showState(true, lbl('VAS_033_MessageNoData', 'No Data'));
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            isDisposed = true;
            $root.remove();
            $card = null;
            $body = null;
            $foot = null;
            $pager = null;
            $pagerPrev = null;
            $pagerNext = null;
            $pagerText = null;
            $busy = null;
            $state = null;
            methodsData = [];
        };
    };

    VAS.VAS_033_PaymentMethodsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_033_PaymentMethodsWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_033_PaymentMethodsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_033_PaymentMethodsWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
