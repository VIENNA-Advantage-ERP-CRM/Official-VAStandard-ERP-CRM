/**
 * Approval Queue - Cash Journal
 * Purpose - Shows in-progress cash journals and latest workflow message.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Approval Queue                       | VAS_053_ApprovalQueue
 *  2  | Pending                              | VAS_053_Pending
 *  3  | View all ->                          | VAS_053_ViewAll
 *  4  | Submitted by                         | VAS_053_SubmittedBy
 *  5  | No in-progress cash journals         | VAS_053_NoData
 *  6  | Unable to load approval queue        | VAS_053_LoadError
 *  7  | Cash Journal                         | VAS_053_CashJournal
 *  8  | High                                 | VAS_053_High
 *  9  | Med                                  | VAS_053_Medium
 * 10  | Low                                  | VAS_053_Low
 * 11  | just now                             | VAS_053_JustNow
 * 12  | ago                                  | VAS_053_Ago
 * 13  | yesterday                            | VAS_053_Yesterday
 * 14  | Loading                              | VAS_053_Loading
 * 15  | Approve                              | VAS_053_Approve
 * 16  | Reject                               | VAS_053_Reject
 * 17  | Session Expired                      | VAS_053_SessionExpired
 * 18  | Previous                             | VAS_053_Previous
 * 19  | Next                                 | VAS_053_Next
 * 20  | Of                                   | VAS_053_Of
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    /**
     * Creates a single ResizeObserver that monitors the dashboard container's
     * width. Whenever the container is resized, it updates the global CSS
     * variable '--dash-inline-size' with the container's current width (in px),
     * so the widget title/header clamp() tracks the dashboard width rather than
     * the viewport. One observer per document is sufficient — every widget reads
     * the same var.
     */
    function ensureDashInlineSizeVar($el) {
        if (window.__vasDashInlineSizeObserver) { return; }
        if (typeof ResizeObserver === 'undefined') { return; }

        var container = $el.closest('.vis-widget-container, [data-dashboard-container]')[0];
        if (!container) { return; }

        var write = function () {
            document.documentElement.style.setProperty('--dash-inline-size', container.clientWidth + 'px');
        };

        window.__vasDashInlineSizeObserver = new ResizeObserver(write);
        window.__vasDashInlineSizeObserver.observe(container);
        write();
    }

    VAS.VAS_053_ApprovalQueueCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;
        var $list = null;
        var resizeObserver = null;
        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== key && text !== '[' + key + ']' ? text : fallback;
        }

        function safeNumber(value) {
            var numberValue = Number(value || 0);
            return isNaN(numberValue) ? 0 : numberValue;
        }

        /* ── Currency label helper ──────────────────────────────────────
           Prefer the currency symbol; fall back to the ISO code when no
           symbol is available (IQD's ISO renders as the short 'ID'). */
        function getCurrencyLabel(currencySymbol, currencyISO) {
            var symbol = String(currencySymbol || '').trim();
            if (symbol) {
                return symbol;
            }

            var iso = String(currencyISO || '').trim();
            return iso.toUpperCase() === 'IQD' ? 'ID' : iso;
        }

        function formatAmount(item) {
            var precision = Number(item && item.stdPrecision);
            var currency = getCurrencyLabel(item && item.currencySymbol, item && item.currencyISO);

            if (isNaN(precision) || precision < 0) {
                precision = 2;
            }

            var numericValue = safeNumber(item && item.amount);
            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
            var sign = numericValue < 0 ? '-' : '';

            return sign + currency + amount;
        }

        function renderAmount($target, item) {
            var precision = Number(item && item.stdPrecision);
            var currency = getCurrencyLabel(item && item.currencySymbol, item && item.currencyISO);

            if (isNaN(precision) || precision < 0) {
                precision = 2;
            }

            var numericValue = safeNumber(item && item.amount);
            var amount = Math.abs(numericValue).toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
            var decimalMatch = amount.match(/([.,]\d+)$/);

            $target.empty()
                .append($('<span>', {
                    'class': 'VAS-cash-amount-prefix',
                    'text': (numericValue < 0 ? '-' : '') + currency
                }))
                .append($('<span>', {
                    'class': 'VAS-cash-amount-main',
                    'text': decimalMatch ? amount.substring(0, amount.length - decimalMatch[1].length) : amount
                }))
                .append($('<span>', {
                    'class': 'VAS-cash-amount-decimal',
                    'text': decimalMatch ? decimalMatch[1] : ''
                }));
        }

        function getApproveIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<polyline points="20 6 9 17 4 12"></polyline>' +
                '</svg>';
        }

        function getRejectIcon() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<line x1="18" y1="6" x2="6" y2="18"></line>' +
                '<line x1="6" y1="6" x2="18" y2="18"></line>' +
                '</svg>';
        }

        function showBusy(show) {
            var $busy = $root.find('#VAS_053_approval-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $busy.addClass('is-visible'); } else { $busy.removeClass('is-visible'); }
        }

        function startWindowById(windowId) {
            if (!windowId) {
                return;
            }

            if (VIS.viewManager && VIS.viewManager.startWindow) {
                VIS.viewManager.startWindow(windowId, null);
            } else if (VIS.AEnv && VIS.AEnv.startWindow) {
                VIS.AEnv.startWindow(windowId, null);
            }
        }

        //function getWindowIdByName(windowName, callback) {
        //    $.ajax({
        //        url: VIS.Application.contextUrl + 'VAS/VAS_053_ApprovalQueueCashJournal/GetWindowIdByName',
        //        type: 'GET',
        //        dataType: 'json',
        //        cache: false,
        //        data: {
        //            windowName: windowName
        //        },
        //        success: function (response) {
        //            callback(safeNumber(response && response.windowId));
        //        },
        //        error: function () {
        //            callback(0);
        //        }
        //    });
        //}

        //function openWindowByName(windowName, fallbackWindowId) {
        //    getWindowIdByName(windowName, function (windowId) {
        //        startWindowById(windowId || fallbackWindowId);
        //    });
        //}

        //function openWorkFlow() {
        //    openWindowByName('WorkFlow', 1000409);
        //}

        function buildLayout() {
            var widgetId = $self.AD_UserHomeWidgetID;

            $root = $('<div>', {
                'class': 'VAS_053_approval-root',
                'id': 'VAS_053_approval-root-' + widgetId
            });

            var $card = $('<section>', {
                'class': 'VAS_053_approval-card',
                'aria-label': lbl('VAS_053_ApprovalQueue', 'Approval Queue')
            });

            var $busy = $('<div>', {
                'class': 'VAS_053_approval-busy vis-busyindicatorouterwrap',
                'id': 'VAS_053_approval-busy-' + widgetId,
                'html': '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
            });

            var $header = $('<div>', {
                'class': 'VAS_053_approval-header'
            });

            var $titleRow = $('<div>', {
                'class': 'VAS_053_approval-title-row'
            });

            var $icon = $(
                '<span class="VAS_053_approval-icon">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
                '<path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path>' +
                '<polyline points="22 4 12 14.01 9 11.01"></polyline>' +
                '</svg>' +
                '</span>'
            );

            var $title = $('<span>', {
                'class': 'VAS_053_approval-title',
                'id': 'VAS_053_approval-title-' + widgetId,
                'text': lbl('VAS_053_ApprovalQueue', 'Approval Queue')
            });

            var $meta = $('<span>', {
                'class': 'VAS_053_approval-meta',
                'id': 'VAS_053_approval-meta-' + widgetId,
                'text': '0 ' + lbl('VAS_053_Pending', 'Pending')
            });

            //var $viewAll = $('<button>', {
            //    'class': 'VAS_053_approval-action',
            //    'type': 'button',
            //    'id': 'VAS_053_approval-view-all-' + widgetId,
            //    'text': lbl('VAS_053_ViewAll', 'View all ->')
            //});

            var $pager = $(
                '<div class="VAS_053_approval-pager">' +
                '<button type="button" class="VAS_053_approval-page-btn" id="VAS_053_approval-prev-' + widgetId + '" aria-label="' + lbl('VAS_053_Previous', 'Previous') + '">&lsaquo;</button>' +
                '<span class="VAS_053_approval-page-text" id="VAS_053_approval-page-text-' + widgetId + '"></span>' +
                '<button type="button" class="VAS_053_approval-page-btn" id="VAS_053_approval-next-' + widgetId + '" aria-label="' + lbl('VAS_053_Next', 'Next') + '">&rsaquo;</button>' +
                '</div>'
            );

            var $footer = $('<div>', {
                'class': 'VAS_053_approval-footer'
            });

            $list = $('<div>', {
                'class': 'VAS_053_approval-list',
                'id': 'VAS_053_approval-list-' + widgetId
            });

            var $state = $('<div>', {
                'class': 'VAS_053_approval-state',
                'id': 'VAS_053_approval-state-' + widgetId
            });

            $titleRow.append($icon).append($title).append($meta);
            $header.append($titleRow);
            $footer.append($pager);
            $card.append($busy).append($header).append($list).append($state).append($footer);
            $root.append($card);

            bindEvents();
            updatePager();
        }

        function bindEvents() {
            //$root.on('click', '#VAS_053_approval-view-all-' + $self.AD_UserHomeWidgetID, function () {
            //    openWorkFlow();
            //});

            $root.on('click', '#VAS_053_approval-prev-' + $self.AD_UserHomeWidgetID, function () {
                if (pageNo <= 1 || totalPages <= 1) {
                    return;
                }

                pageNo--;
                loadData();
            });

            $root.on('click', '#VAS_053_approval-next-' + $self.AD_UserHomeWidgetID, function () {
                if (totalPages <= 1 || pageNo >= totalPages) {
                    return;
                }

                pageNo++;
                loadData();
            });
        }

        function updatePager() {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var $prev = $root.find('#VAS_053_approval-prev-' + widgetId);
            var $next = $root.find('#VAS_053_approval-next-' + widgetId);
            var $pageText = $root.find('#VAS_053_approval-page-text-' + widgetId);
            var $footer = $root.find('.VAS_053_approval-footer');

            $footer.toggle(totalPages > 1);

            if ($pageText) {
                $pageText.text(totalPages > 1 ? pageNo + ' ' + lbl('VAS_053_Of', 'of') + ' ' + totalPages : '');
            }

            if ($prev) {
                $prev.prop('disabled', pageNo <= 1 || totalPages <= 1);
            }

            if ($next) {
                $next.prop('disabled', totalPages <= 1 || pageNo >= totalPages);
            }
        }

        function updateAdaptivePageSize(shouldReload) {
            if (!$list || !$list[0]) {
                return;
            }

            var nextPageSize = Math.max(3, Math.floor($list[0].clientHeight / 72));

            if (nextPageSize === pageSize) {
                return;
            }

            var firstVisibleRecord = ((pageNo - 1) * pageSize) + 1;
            pageSize = nextPageSize;
            pageNo = Math.max(1, Math.ceil(firstVisibleRecord / pageSize));

            if (shouldReload) {
                loadData();
            }
        }

        function setupAdaptivePagination() {
            updateAdaptivePageSize(false);

            window.setTimeout(function () {
                updateAdaptivePageSize(true);
            }, 0);

            if (!window.ResizeObserver || !$list || !$list[0]) {
                return;
            }

            resizeObserver = new ResizeObserver(function () {
                updateAdaptivePageSize(true);
            });

            resizeObserver.observe($list[0]);
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            totalPages = 0;
            updatePager();
            $root.find('#VAS_053_approval-state-' + widgetId).text(message || '').addClass('is-visible');
            $root.find('#VAS_053_approval-list-' + widgetId).empty();
        }

        function renderData(data) {
            if (!$root || isDisposed) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
            var items = data.items || [];
            var $list = $root.find('#VAS_053_approval-list-' + widgetId);

            pageNo = safeNumber(data.pageNo) || pageNo;
            totalPages = safeNumber(data.totalPages);
            if (pageNo < 1) { pageNo = 1; }
            if (totalPages > 0 && pageNo > totalPages) { pageNo = totalPages; }

            $root.find('#VAS_053_approval-state-' + widgetId).removeClass('is-visible').text('');
            var title = data.title || lbl('VAS_053_ApprovalQueue', 'Approval Queue');
            var metaText = safeNumber(data.pendingCount).toLocaleString(window.navigator.language) + ' ' + (data.pendingText || lbl('VAS_053_Pending', 'Pending'));
            $root.find('#VAS_053_approval-title-' + widgetId).text(title).attr('title', title);
            $root.find('#VAS_053_approval-meta-' + widgetId).text(metaText).attr('title', metaText);
            $root.find('#VAS_053_approval-view-all-' + widgetId).text(data.viewAllText || lbl('VAS_053_ViewAll', 'View all ->'));
            updatePager();

            $list.empty();

            if (!items.length) {
                setState('No data');
                return;
            }

            $.each(items, function (index, item) {
                var $row = $('<div>', {
                    'class': 'VAS_053_approval-row'
                });

                var $main = $('<div>', {
                    'class': 'VAS_053_approval-row-main'
                });

                var $top = $('<div>', {
                    'class': 'VAS_053_approval-row-top'
                });

                var $title = $('<span>', {
                    'class': 'VAS_053_approval-row-title',
                    'text': item.title || item.documentNo || '-',
                    'title': item.title || item.documentNo || '-'
                });

                var $priority = $('<span>', {
                    'class': 'VAS_053_approval-priority VAS_053_approval-priority-' + (item.priorityClass || 'low'),
                    'text': item.priorityText || ''
                });

                var subText = (data.submittedByText || lbl('VAS_053_SubmittedBy', 'Submitted by')) + ' ' + (item.createdByName || '-') + (item.relativeTime ? ' · ' + item.relativeTime : '');
                var $sub = $('<div>', {
                    'class': 'VAS_053_approval-row-sub',
                    'text': subText,
                    'title': subText
                });

                var $message = $('<div>', {
                    'class': 'VAS_053_approval-message',
                    'text': item.workflowMessage || '',
                    'title': item.workflowMessage || ''
                });

                var $amount = $('<div>', {
                    'class': 'VAS_053_approval-amount',
                    'title': formatAmount(item)
                });

                renderAmount($amount, item);

                //var $approve = $('<button>', {
                //    'type': 'button',
                //    'class': 'VAS_053_approval-btn VAS_053_approval-btn-approve',
                //    'aria-label': lbl('VAS_053_Approve', 'Approve')
                //}).html(getApproveIcon());

                //var $reject = $('<button>', {
                //    'type': 'button',
                //    'class': 'VAS_053_approval-btn VAS_053_approval-btn-reject',
                //    'aria-label': lbl('VAS_053_Reject', 'Reject')
                //}).html(getRejectIcon());

                $top.append($title).append($priority).append($amount);
                $main.append($top).append($sub);

                if (item.workflowMessage) {
                    $main.append($message);
                }

                $row.append($main);
                $list.append($row);
            });
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
                url: VIS.Application.contextUrl + 'VAS/VAS_053_ApprovalQueueCashJournal/GetApprovalQueue',
                type: 'GET',
                dataType: 'json',
                data: {
                    pageNo: pageNo,
                    pageSize: pageSize
                },
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    if (!response) {
                        setState(lbl('VAS_053_LoadError', 'Unable to load approval queue'));
                        return;
                    }

                    if (response.error === 'VAS_053_SessionExpired') {
                        setState(lbl('VAS_053_SessionExpired', 'Session Expired'));
                        return;
                    }

                    if (response.success === false || response.error) {
                        setState(response.error || lbl('VAS_053_LoadError', 'Unable to load approval queue'));
                        return;
                    }

                    renderData(response);
                },
                error: function () {
                    if (!isDisposed) {
                        setState(lbl('VAS_053_LoadError', 'Unable to load approval queue'));
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
            setupAdaptivePagination();
            loadData();
        };

        this.refreshWidget = function () {
            pageNo = 1;
            loadData();
        };

        this.disposeComponent = function () {
            isDisposed = true;

            if (ajaxRequest && ajaxRequest.readyState !== 4) {
                ajaxRequest.abort();
            }

            if ($root) {
                $root.off();
                $root.remove();
            }

            if (resizeObserver) {
                resizeObserver.disconnect();
            }

            ajaxRequest = null;
            resizeObserver = null;
            $list = null;
            $root = null;
            $self = null;
        };

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_053_ApprovalQueueCashJournalWidget.prototype.init = function (windowNo, frame) {
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

        ensureDashInlineSizeVar(this.getRoot());
    };

    VAS.VAS_053_ApprovalQueueCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_053_ApprovalQueueCashJournalWidget.prototype.widgetFirevalueChanged = function (value) {
        if (this.listener && typeof this.listener.widgetFirevalueChanged === "function") {
            this.listener.widgetFirevalueChanged(value);
        }
        else if (this.getRoot && this.getRoot()) {
            this.getRoot().trigger('widgetFirevalueChanged', value);
        }
    };

    VAS.VAS_053_ApprovalQueueCashJournalWidget.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    };

    VAS.VAS_053_ApprovalQueueCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
