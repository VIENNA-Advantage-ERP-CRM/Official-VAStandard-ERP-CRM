/**
 * GL Journal Top Ledger Movement Widget
 * Purpose - Horizontal bar chart of the top accounts by absolute net movement.
 *
 * -- Labels / Message Keys --------------------------------------------
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Top Ledger Movement                  | VAS_043_TopLedgerMovement
 *  3  | Month                                | VAS_043_Month
 *  4  | YTD                                  | VAS_043_YTD
 *  5  | Loading                              | VIS_Loading
 *  6  | No Data                              | VIS_NoData
 *  7  | Previous                             | VIS_Previous
 *  8  | Next                                 | VIS_Next
 *  9  | Of                                   | VIS_Of
 * 10  | Error Loading Data                   | VIS_Error
 * ---------------------------------------------------------------------
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_043_TopLedgerMovement, VAS_043_Month, VAS_043_YTD
    // ───────────────────────────────────────────────────────────────────────────

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    function esc(str) {
        return String(str || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // Precision is always dynamic from C_Currency.StdPrecision — never hardcoded.
    function fmtAmt(amount, precision) {
        var stdPrecision = VIS.Env.getCtx().getStdPrecision();
        var prec = (typeof precision === 'number' && precision >= 0) ? precision : stdPrecision;
        return parseFloat(amount || 0).toLocaleString(window.navigator.language, {
            minimumFractionDigits: prec,
            maximumFractionDigits: prec
        });
    }

    function resolveTotalCount(totalCount, visibleCount, pageSize, totalPages) {
        var total = Number(totalCount || 0);

        if (!isNaN(total) && total > 0) {
            return total;
        }

        var rows = Number(visibleCount || 0);

        if (isNaN(rows) || rows <= 0) {
            return 0;
        }

        var size = Math.max(parseInt(pageSize || rows || 1, 10), 1);
        var pages = Math.max(parseInt(totalPages || 1, 10), 1);

        if (pages > 1) {
            return Math.max(((pages - 1) * size) + rows, rows);
        }

        return rows;
    }

    function formatRangeText(pageNo, pageSize, totalCount) {
        var total = Number(totalCount || 0);

        if (isNaN(total) || total <= 0) {
            return '';
        }

        var page = Math.max(parseInt(pageNo || 1, 10), 1);
        var size = Math.max(parseInt(pageSize || total, 10), 1);
        var start = ((page - 1) * size) + 1;

        if (start > total) {
            start = total;
        }

        var end = Math.min(start + size - 1, total);

        return 'Showing ' + start + '-' + end + ' of ' + total;
    }

    // ──────────────────────────────────────────────────────────────────────────
    VAS.VAS_043_GLJournalTopMovementWidget = function () {

        this.frame;
        this.windowNo;
        var $self        = this;
        var $root        = $('<div class="VAS-gljtm-root">');
        var activePeriod = 'month';
        var currentData  = null;
        var pageNo       = 1;
        var pageSize     = 4;
        var totalPages   = 0;
        var totalCount   = 0;
        var refreshTimer = null;
        var baseUrl      = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            showBusy(true);
            loadData();
            refreshTimer = setInterval(function () { $self.refreshWidget(); }, 1000 * 60 * 5);
        };

        function createBusyIndicator() {
            var $bsy = $('<div id="VAS-gljtm-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljtm-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            var listIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<line x1="3" y1="12" x2="21" y2="12"></line>'
                + '<line x1="3" y1="6" x2="21" y2="6"></line>'
                + '<line x1="3" y1="18" x2="21" y2="18"></line>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="VAS-gljtm-card">'

                + '<div class="w-head">'
                +   '<div class="VAS-gljtm-icon">' + listIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_043_TopLedgerMovement', 'Top Ledger Movement') + '</div>'
                +   '<div class="VAS-gljtm-period-group" role="group" aria-label="Period">'
                +     '<button type="button" class="VAS-gljtm-period-btn" data-period="ytd">'
                +       lbl('VAS_043_YTD', 'YTD')
                +     '</button>'
                +     '<button type="button" class="VAS-gljtm-period-btn is-active" data-period="month">'
                +       lbl('VAS_043_Month', 'Month')
                +     '</button>'
                +   '</div>'
                + '</div>'

                + '<div class="VAS-gljtm-body" id="VAS-gljtm-body-' + id + '"></div>'
                + renderPager()

                + '</div>';

            $root.append(html);

            $root.on('click', '.VAS-gljtm-period-btn', function () {
                var nextPeriod = String($(this).data('period') || 'month');

                if (nextPeriod === activePeriod) {
                    return;
                }

                activePeriod = nextPeriod;
                pageNo = 1;
                $root.find('.VAS-gljtm-period-btn').removeClass('is-active');
                $(this).addClass('is-active');
                showBusy(true);
                loadData();
            });

            $root.on('click', '.VAS-gljtm-page-prev', function () {
                if (pageNo <= 1 || !currentData) { return; }
                pageNo -= 1;
                renderBars(currentData);
            });

            $root.on('click', '.VAS-gljtm-page-next', function () {
                if (totalPages <= 1 || pageNo >= totalPages || !currentData) { return; }
                pageNo += 1;
                renderBars(currentData);
            });
        }

        function normalizeResponse(result) {
            if (!result) {
                return null;
            }

            if (result.d) {
                result = result.d;
            }

            if (typeof result === 'string') {
                try {
                    result = JSON.parse(result);
                } catch (e1) {
                    return null;
                }
            }

            if (typeof result === 'string') {
                try {
                    result = JSON.parse(result);
                } catch (e2) {
                    return null;
                }
            }

            return result;
        }

        function loadData() {
            showLoading();

            $.ajax({
                url: baseUrl + 'VAS/VAS_043_GLJournalTopMovementWidget/GetTopMovement',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                data     : { period: activePeriod, pageNo: pageNo },
                success  : function (result) {
                    var data = normalizeResponse(result);

                    if (data && data.Accounts) {
                        currentData = data;
                        renderBars(data);
                    } else {
                        showError();
                    }

                    showBusy(false);
                },
                error: function () {
                    showError();
                    showBusy(false);
                }
            });
        }

        function showLoading() {
            var id = $self.AD_UserHomeWidgetID;

            totalPages = 0;
            totalCount = 0;
            $root.find('#VAS-gljtm-body-' + id).html(
                '<div class="VAS-gljtm-loading">' + esc(lbl('VIS_Loading', 'Loading...')) + '</div>'
            );
        }

        function renderBars(data) {
            var id       = $self.AD_UserHomeWidgetID;
            var accounts = data.Accounts || [];
            var sym      = esc(data.currencySymbol || data.CurSymbol || data.currencyISO || data.ISOCode || '');
            var prec     = data.stdPrecision || data.StdPrecision;
            var $body    = $root.find('#VAS-gljtm-body-' + id);

            if (accounts.length === 0) {
                $body.addClass('is-empty');
                $body.html('<div class="VAS-gljtm-empty">' + lbl('VIS_NoData', 'No data available.') + '</div>');
                totalPages = 0;
                totalCount = 0;
                updatePager();
                return;
            }

            $body.removeClass('is-empty');

            /* The service returns the full top-N set, so page it here. */
            totalCount = accounts.length;
            totalPages = Math.max(Math.ceil(totalCount / pageSize), 1);

            if (isNaN(pageNo) || pageNo < 1) {
                pageNo = 1;
            }

            if (pageNo > totalPages) {
                pageNo = totalPages;
            }

            var pageAccounts = accounts.slice(
                (pageNo - 1) * pageSize,
                pageNo * pageSize
            );
            var html = '<div class="VAS-gljtm-list">';
            for (var i = 0; i < pageAccounts.length; i++) {
                var a       = pageAccounts[i];
                var fillCls = 'VAS-gljtm-fill' + (a.IsCredit ? ' VAS-gljtm-fill-cr' : '');
                var valStr  = sym + fmtAmt(a.NetMovement, prec);
                var rowInfo = esc((a.AccountCode || '') + ' ' + (a.AccountName || '')
                    + ' - Net Movement: ' + valStr
                    + ', Debit: ' + sym + fmtAmt(a.TotalDebit, prec)
                    + ', Credit: ' + sym + fmtAmt(a.TotalCredit, prec));

                html += '<div class="VAS-gljtm-row" title="' + rowInfo + '">'
                    +     '<div class="VAS-gljtm-row-head">'
                    +       '<span class="VAS-gljtm-label">'
                    +         '<span class="VAS-gljtm-acct">' + esc(a.AccountCode) + '</span>'
                    +         esc(a.AccountName)
                    +       '</span>'
                    +       '<span class="VAS-gljtm-val">' + esc(valStr) + '</span>'
                    +     '</div>'
                    +     '<div class="VAS-gljtm-track" title="' + rowInfo + '">'
                    +       '<div class="' + fillCls + '" style="--bar-w:' + a.BarPct + '%"></div>'
                    +     '</div>'
                    +   '</div>';
            }
            html += '</div>';
            $body.html(html);
            updatePager();
        }

        function renderPager() {
            return '<div class="VAS-gljtm-pager">'
                + '<span class="VAS-gljtm-page-text"></span>'
                + '<button type="button" class="VAS-gljtm-page-btn VAS-gljtm-page-prev" aria-label="' + esc(lbl('VIS_Previous', 'Previous')) + '">&#8249;</button>'
                + '<span class="VAS-gljtm-page-count"></span>'
                + '<button type="button" class="VAS-gljtm-page-btn VAS-gljtm-page-next" aria-label="' + esc(lbl('VIS_Next', 'Next')) + '">&#8250;</button>'
                + '</div>';
        }

        function updatePager() {
            var $pageText = $root.find('.VAS-gljtm-page-text');
            var $pageCount = $root.find('.VAS-gljtm-page-count');
            var $prevBtn = $root.find('.VAS-gljtm-page-prev');
            var $nextBtn = $root.find('.VAS-gljtm-page-next');

            if (totalCount > 0) {
                $pageText.text(formatRangeText(pageNo, pageSize, totalCount));
                $pageCount.text(pageNo + ' ' + lbl('VIS_Of', 'of') + ' ' + totalPages);
            } else {
                $pageText.text('');
                $pageCount.text('');
            }

            $prevBtn.prop('disabled', pageNo <= 1 || totalPages <= 1);
            $nextBtn.prop('disabled', totalPages <= 1 || pageNo >= totalPages);
        }

        function showError() {
            var id = $self.AD_UserHomeWidgetID;
            $root.find('#VAS-gljtm-body-' + id).addClass('is-empty').html(
                '<div class="VAS-gljtm-empty">' + lbl('VIS_Error', 'Error loading data.') + '</div>'
            );
            totalPages = 0;
            totalCount = 0;
            updatePager();
        }

        this.refreshWidget    = function () { showBusy(true); loadData(); };
        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () {
            if (refreshTimer) {
                clearInterval(refreshTimer);
                refreshTimer = null;
            }

            if ($root) {
                $root.off();
                $root.remove();
            }

            currentData = null;
        };
    };

    VAS.VAS_043_GLJournalTopMovementWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_043_GLJournalTopMovementWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_043_GLJournalTopMovementWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_043_GLJournalTopMovementWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
