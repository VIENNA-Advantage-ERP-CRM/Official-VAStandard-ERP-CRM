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
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {
    VAS.VAS_053_ApprovalQueueCashJournalWidget = function () {
        var $self = this;
        var $root = null;
        var isDisposed = false;
        var ajaxRequest = null;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== key && text !== '[' + key + ']' ? text : fallback;
        }

        function safeNumber(value) {
            var numberValue = Number(value || 0);
            return isNaN(numberValue) ? 0 : numberValue;
        }

        function formatAmount(item) {
            var precision = Number(item && item.stdPrecision);
            var symbol = item && item.currencySymbol ? item.currencySymbol : '';

            if (isNaN(precision) || precision < 0) {
                precision = 2;
            }

            return symbol + safeNumber(item && item.amount).toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
        }

        function truncateText(text, maxLength) {
            var value = text || '';

            value = value.replace(/\s+/g, ' ').trim();

            if (value.length <= maxLength) {
                return value;
            }

            return value.substring(0, maxLength - 3) + '...';
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
                'class': 'VAS_053_approval-busy',
                'id': 'VAS_053_approval-busy-' + widgetId,
                'text': lbl('VAS_053_Loading', 'Loading')
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

            var $viewAll = $('<button>', {
                'class': 'VAS_053_approval-action',
                'type': 'button',
                'id': 'VAS_053_approval-view-all-' + widgetId,
                'text': lbl('VAS_053_ViewAll', 'View all ->')
            });

            var $body = $('<div>', {
                'class': 'VAS_053_approval-list',
                'id': 'VAS_053_approval-list-' + widgetId
            });

            var $state = $('<div>', {
                'class': 'VAS_053_approval-state',
                'id': 'VAS_053_approval-state-' + widgetId
            });

            $titleRow.append($icon).append($title).append($meta);
            $header.append($titleRow).append($viewAll);
            $card.append($busy).append($header).append($body).append($state);
            $root.append($card);
        }

        function setState(message) {
            if (!$root) {
                return;
            }

            var widgetId = $self.AD_UserHomeWidgetID;
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

            $root.find('#VAS_053_approval-state-' + widgetId).removeClass('is-visible').text('');
            $root.find('#VAS_053_approval-title-' + widgetId).text(data.title || lbl('VAS_053_ApprovalQueue', 'Approval Queue'));
            $root.find('#VAS_053_approval-meta-' + widgetId).text(safeNumber(data.pendingCount).toLocaleString(window.navigator.language) + ' ' + (data.pendingText || lbl('VAS_053_Pending', 'Pending')));
            $root.find('#VAS_053_approval-view-all-' + widgetId).text(data.viewAllText || lbl('VAS_053_ViewAll', 'View all ->'));

            $list.empty();

            if (!items.length) {
                setState(data.noDataText || lbl('VAS_053_NoData', 'No in-progress cash journals'));
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
                    'text': item.title || item.documentNo || '-'
                });

                var $priority = $('<span>', {
                    'class': 'VAS_053_approval-priority VAS_053_approval-priority-' + (item.priorityClass || 'low'),
                    'text': item.priorityText || ''
                });

                var subText = (data.submittedByText || lbl('VAS_053_SubmittedBy', 'Submitted by')) + ' ' + (item.createdByName || '-') + ' · ' + (item.relativeTime || '');
                var $sub = $('<div>', {
                    'class': 'VAS_053_approval-row-sub',
                    'text': subText
                });

                var $message = $('<div>', {
                    'class': 'VAS_053_approval-message',
                    'text': truncateText(item.workflowMessage || '', 68),
                    'title': item.workflowMessage || ''
                });

                var $amount = $('<div>', {
                    'class': 'VAS_053_approval-amount',
                    'text': formatAmount(item)
                });

                var $side = $('<div>', {
                    'class': 'VAS_053_approval-side'
                });

                var $actions = $('<div>', {
                    'class': 'VAS_053_approval-actions'
                });

                var $approve = $('<button>', {
                    'type': 'button',
                    'class': 'VAS_053_approval-btn VAS_053_approval-btn-approve',
                    'aria-label': lbl('VAS_053_Approve', 'Approve')
                }).html(getApproveIcon());

                var $reject = $('<button>', {
                    'type': 'button',
                    'class': 'VAS_053_approval-btn VAS_053_approval-btn-reject',
                    'aria-label': lbl('VAS_053_Reject', 'Reject')
                }).html(getRejectIcon());

                $top.append($title).append($priority);
                $main.append($top).append($sub);

                if (item.workflowMessage) {
                    $main.append($message);
                }

                $actions.append($approve).append($reject);
                $side.append($amount).append($actions);
                $row.append($main).append($side);
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
                cache: false,
                success: function (response) {
                    if (isDisposed) {
                        return;
                    }

                    if (!response) {
                        setState(lbl('VAS_053_LoadError', 'Unable to load approval queue'));
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

            if ($root) {
                $root.off();
                $root.remove();
            }

            ajaxRequest = null;
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
    };

    VAS.VAS_053_ApprovalQueueCashJournalWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_053_ApprovalQueueCashJournalWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };
})(VAS, jQuery);
