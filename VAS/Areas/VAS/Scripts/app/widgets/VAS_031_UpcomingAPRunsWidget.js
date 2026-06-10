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
        var $busy;
        var $state;
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

            $iconBox.append($icon);
            $titleRow.append($iconBox).append($title);
            $headLeft.append($titleRow).append($sub);
            $head.append($headLeft);

            $body = $('<div class="vas-upcoming-ap-runs-body">');
            $busy = $('<div class="vas-upcoming-ap-runs-busy">').text(lbl('VAS_031_MessageLoading', 'Loading'));
            $state = $('<div class="vas-upcoming-ap-runs-state-message">');

            $card.append($head).append($body).append($busy).append($state);
            $root.empty().append($card);
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
            var runs = $.isArray(data.runs)
                ? $.grep(data.runs, function (run) {
                    var amount = Number(pickRunValue(run, 'totalAmount', 'amount') || 0);
                    var paymentCount = Number(run.paymentCount || 0);

                    return (!isNaN(amount) && amount > 0) || (!isNaN(paymentCount) && paymentCount > 0);
                })
                : [];

            if (runs.length === 0) {
                setNoData();
                return;
            }

            showState(false, '');
            $body.empty();

            for (var i = 0; i < runs.length && i < 3; i++) {
                $body.append(createRunRow(runs[i]));
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

            $info.append($title).append($meta);
            $row.append($bar).append($info).append($amount);

            return $row;
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

            return paymentMethodName ;
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
            $root.remove();
            $card = null;
            $body = null;
            $busy = null;
            $state = null;
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
