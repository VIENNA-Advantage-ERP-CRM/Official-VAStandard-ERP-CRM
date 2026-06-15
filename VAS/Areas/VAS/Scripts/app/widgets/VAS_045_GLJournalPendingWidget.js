/**
 * GL Journal Pending Action Queue Widget
 * Purpose - Displays GL journals awaiting user action.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                               | Message Key
 * ----+--------------------------------------------+-------------------------------
 *  1  | Pending Action Queue                       | VAS_045_PendingActionQueue
 *  2  | items                                      | VAS_045_Items
 *  3  | overdue                                    | VAS_045_Overdue
 *  4  | Previous                                   | VIS_Previous
 *  5  | Next                                       | VIS_Next
 *  6  | Close                                      | VAS_Close
 *  7  | Approve                                    | VAS_041_Approve
 *  8  | Post journal                               | VAS_041_PostJournal
 *  9  | Error loading data.                        | VIS_Error
 * 10  | No pending journals.                       | VIS_NoData
 * 11  | Journal details not found                   | VAS_045_JournalDetailsNotFound
 * 12  | Journal object was not returned from server | VAS_045_JournalObjectNotReturned
 * 13  | No journal lines were returned from server  | VAS_045_NoJournalLinesReturned
 * 14  | Journal Lines                               | VAS_045_JournalLines
 * 15  | Journal No.                                 | VAS_045_JournalNo
 * 16  | Date                                        | VAS_045_Date
 * 17  | Status                                      | VAS_045_Status
 * 18  | Accounting Book                             | VAS_045_AccountingBook
 * 19  | Total Debit                                 | VAS_045_TotalDebit
 * 20  | Total Credit                                | VAS_045_TotalCredit
 * 21  | Description                                 | VAS_045_Description
 * 22  | Created By                                  | VAS_045_CreatedBy
 * 23  | Account                                     | VAS_045_Account
 * 24  | Debit                                       | VAS_045_Debit
 * 25  | Credit                                      | VAS_045_Credit
 * 26  | Cost Center                                 | VAS_045_CostCenter
 * 27  | Business Partner                            | VAS_045_BusinessPartner
 * 28  | Product                                     | VAS_045_Product
 * 29  | Project                                     | VAS_045_Project
 * 30  | Total                                       | VAS_045_Total
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_045_GLJournalPendingWidget = function () {

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $root = $('<div class="VAS-gljpq-root">');

        var $detailDialog = null;
        var $detailBody = null;
        var $detailBusy = null;

        var currentData = null;
        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;
        var refreshTimer = null;
        var baseUrl = VIS.Application.contextUrl;

        var PILL_CLASS = {
            'DR': 'VAS-gljpq-pill-draft',
            'CO': 'VAS-gljpq-pill-posted',
            'CL': 'VAS-gljpq-pill-posted',
            'IP': 'VAS-gljpq-pill-submit',
            'AP': 'VAS-gljpq-pill-posted',
            'NA': 'VAS-gljpq-pill-pending',
            'RE': 'VAS-gljpq-pill-returned'
        };

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            loadData();

            refreshTimer = setInterval(function () {
                $self.refreshWidget();
            }, 1000 * 60 * 5);
        };

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        function esc(str) {
            return String(str || '')
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;');
        }

        function fmtAmt(amount, precision) {
            var stdPrecision = 2;

            if (VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            if (typeof precision === 'number' && precision >= 0) {
                stdPrecision = precision;
            }

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                stdPrecision = 2;
            }

            return Number(amount || 0).toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
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
                    console.log('First JSON parse failed:', e1, result);
                    return null;
                }
            }

            if (typeof result === 'string') {
                try {
                    result = JSON.parse(result);
                } catch (e2) {
                    console.log('Second JSON parse failed:', e2, result);
                    return null;
                }
            }

            return result;
        }

        function getJournalId(item) {
            if (!item) {
                return 0;
            }

            var id = item.GL_Journal_ID;

            if (id === undefined || id === null || id === '') {
                id = item.JournalID;
            }

            if (id === undefined || id === null || id === '') {
                id = item.journalId;
            }

            if (id === undefined || id === null || id === '') {
                id = item.GLJournalID;
            }

            if (id === undefined || id === null || id === '') {
                id = item.GL_JournalId;
            }

            id = parseInt(id, 10);

            if (isNaN(id)) {
                id = 0;
            }

            return id;
        }

        function createBusyIndicator() {
            var $bsy = $(
                '<div id="VAS-gljpq-busy-' + $self.AD_UserHomeWidgetID + '" class="vis-busyindicatorouterwrap">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>'
            );

            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljpq-busy-' + $self.AD_UserHomeWidgetID);

            if (show) {
                $b.show();
            } else {
                $b.hide();
            }
        }

        function createWidget() {
            var id = $self.AD_UserHomeWidgetID;

            var clockIcon =
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"' +
                ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<circle cx="12" cy="12" r="10"></circle>' +
                '<polyline points="12 6 12 12 16 14"></polyline>' +
                '</svg>';

            var html =
                '<div class="VAS-gljpq-card">' +
                '<div class="w-head">' +
                '<div class="VAS-gljpq-icon">' + clockIcon + '</div>' +
                '<div class="w-title">' + lbl('VAS_045_PendingActionQueue', 'Pending Action Queue') + '</div>' +
                '<span class="VAS-gljpq-count" id="VAS-gljpq-count-' + id + '"></span>' +
                '<div class="VAS-gljpq-pager">' +
                '<button type="button" class="VAS-gljpq-page-btn VAS-gljpq-prev" aria-label="' + esc(lbl('VIS_Previous', 'Previous')) + '">&#8249;</button>' +
                '<span class="VAS-gljpq-page-text"></span>' +
                '<button type="button" class="VAS-gljpq-page-btn VAS-gljpq-next" aria-label="' + esc(lbl('VIS_Next', 'Next')) + '">&#8250;</button>' +
                '</div>' +
                '</div>' +
                '<div class="VAS-gljpq-body" id="VAS-gljpq-body-' + id + '"></div>' +
                '</div>';

            $root.append(html);

            $root.on('click', '.VAS-gljpq-prev', function () {
                if (pageNo <= 1) {
                    return;
                }

                pageNo--;
                render(currentData || {});
            });

            $root.on('click', '.VAS-gljpq-next', function () {
                if (totalPages <= 1 || pageNo >= totalPages) {
                    return;
                }

                pageNo++;
                render(currentData || {});
            });

            createDetailDialog(clockIcon);
        }

        function loadData() {
            showBusy(true);

            $.ajax({
                url: baseUrl + 'VAS/VAS_045_GLJournalPendingWidget/GetPendingQueue',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (result) {
                    var data = normalizeResponse(result);

                    if (!data) {
                        showError();
                        return;
                    }

                    if (data.success === false || data.error) {
                        showError(data.errorText || data.error);
                        return;
                    }

                    if (!data.Queue) {
                        showError();
                        return;
                    }

                    currentData = data;
                    pageNo = 1;
                    render(data);
                },
                error: function (xhr) {
                    showError(xhr && xhr.responseText ? xhr.responseText : null);
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        function render(data) {
            var id = $self.AD_UserHomeWidgetID;
            var $body = $root.find('#VAS-gljpq-body-' + id);
            var queue = data.Queue || [];
            var prec = data.StdPrecision;

            $root.find('#VAS-gljpq-count-' + id).text(
                Number(data.TotalCount || queue.length || 0).toLocaleString(window.navigator.language) +
                ' ' +
                lbl('VAS_045_Items', 'items')
            );

            if (queue.length === 0) {
                $body.html('<div class="VAS-gljpq-empty">' + esc(lbl('VIS_NoData', 'No pending journals.')) + '</div>');
                totalPages = 0;
                updatePager();
                return;
            }

            totalPages = Math.ceil(queue.length / pageSize);

            if (pageNo > totalPages) {
                pageNo = totalPages;
            }

            if (pageNo < 1) {
                pageNo = 1;
            }

            var start = (pageNo - 1) * pageSize;
            var pageQueue = queue.slice(start, start + pageSize);
            var html = '<div class="VAS-gljpq-list">';

            for (var i = 0; i < pageQueue.length; i++) {
                var item = pageQueue[i];
                var journalId = getJournalId(item);

                var titleStr = esc(item.DocumentNo);

                if (item.Description) {
                    titleStr += ' · ' + esc(item.Description);
                }

                var ageLabel = item.IsOverdue
                    ? esc(item.AgeStr) + ' ' + esc(lbl('VAS_045_Overdue', 'overdue'))
                    : esc(item.AgeStr);

                var metaParts = [esc(item.ActionLabel), ageLabel];

                if (item.UserName) {
                    metaParts.push(esc(item.UserName));
                }

                var amountStr = fmtAmt(item.TotalDebit, prec);
                var markerType = esc(item.MarkerType || 'normal');

                var itemInfo = esc(
                    String(item.DocumentNo || '') +
                    ' - ' +
                    String(item.ActionLabel || '') +
                    ', ' +
                    String(item.AgeStr || '') +
                    ', ' +
                    amountStr
                );

                html +=
                    '<div class="VAS-gljpq-item" data-journal-id="' + journalId + '" title="' + itemInfo + '">' +
                    '<div class="VAS-gljpq-mrk VAS-gljpq-mrk-' + markerType + '"></div>' +
                    '<div class="VAS-gljpq-body-row">' +
                    '<div class="VAS-gljpq-title">' + titleStr + '</div>' +
                    '<div class="VAS-gljpq-meta">' + metaParts.join(' · ') + '</div>' +
                    '</div>' +
                    '<span class="VAS-gljpq-amt">' + esc(amountStr) + '</span>' +
                    '</div>';
            }

            html += '</div>';

            $body.html(html);

            $body.find('.VAS-gljpq-item').off('click').on('click', function () {
                var journalId = parseInt($(this).attr('data-journal-id'), 10);
                openDetailDialog(journalId);
            });

            updatePager();
        }

        function updatePager() {
            var $pageText = $root.find('.VAS-gljpq-page-text');
            var $prevBtn = $root.find('.VAS-gljpq-prev');
            var $nextBtn = $root.find('.VAS-gljpq-next');

            if (totalPages > 1) {
                $pageText.text(pageNo + ' / ' + totalPages);
            } else {
                $pageText.text('');
            }

            $prevBtn.prop('disabled', pageNo <= 1 || totalPages <= 1);
            $nextBtn.prop('disabled', totalPages <= 1 || pageNo >= totalPages);
        }

        function createDetailDialog(svgIcon) {
            var id = $self.AD_UserHomeWidgetID;

            $detailDialog = $(
                '<div class="VAS-gljpq-dialog" id="VAS-gljpq-dialog-' + id + '" role="dialog" aria-modal="true">' +
                '<div class="VAS-gljpq-dialog-scrim"></div>' +
                '<div class="VAS-gljpq-dialog-card">' +

                '<div class="VAS-gljpq-dialog-head">' +
                '<div class="VAS-gljpq-dialog-icon">' + svgIcon + '</div>' +
                '<div class="VAS-gljpq-dialog-title-wrap">' +
                '<div class="VAS-gljpq-dialog-title" id="VAS-gljpq-dialog-title-' + id + '">&mdash;</div>' +
                '<div class="VAS-gljpq-dialog-sub" id="VAS-gljpq-dialog-sub-' + id + '">&mdash;</div>' +
                '</div>' +
                '<button type="button" class="VAS-gljpq-dialog-close-x" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' +
                '<line x1="18" y1="6" x2="6" y2="18"></line>' +
                '<line x1="6" y1="6" x2="18" y2="18"></line>' +
                '</svg>' +
                '</button>' +
                '</div>' +

                '<div class="VAS-gljpq-dialog-body">' +
                '<div class="VAS-gljpq-dialog-busy">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>' +
                '<div class="VAS-gljpq-detail-content" id="VAS-gljpq-detail-body-' + id + '"></div>' +
                '</div>' +

                '<div class="VAS-gljpq-dialog-footer">' +
                '<button type="button" class="VAS-gljpq-dialog-secondary VAS-gljpq-detail-close">' + esc(lbl('VAS_Close', 'Close')) + '</button>' +
                '<div class="VAS-gljpq-dialog-actions">' +
                '<button type="button" class="VAS-gljpq-dialog-secondary VAS-gljpq-action-approve">' + esc(lbl('VAS_041_Approve', 'Approve')) + '</button>' +
                '<button type="button" class="VAS-gljpq-dialog-primary VAS-gljpq-action-post">' + esc(lbl('VAS_041_PostJournal', 'Post journal')) + '</button>' +
                '</div>' +
                '</div>' +

                '</div>' +
                '</div>'
            );

            $detailDialog.hide();

            $detailBody = $detailDialog.find('#VAS-gljpq-detail-body-' + id);
            $detailBusy = $detailDialog.find('.VAS-gljpq-dialog-busy');

            showDetailBusy(false);

            $detailDialog
                .find('.VAS-gljpq-dialog-close-x, .VAS-gljpq-detail-close, .VAS-gljpq-dialog-scrim')
                .on('click', closeDetailDialog);

            $(document).on('keydown.VAS-gljpq-' + id, function (e) {
                if (e.key === 'Escape' && $detailDialog && $detailDialog.is(':visible')) {
                    closeDetailDialog();
                }
            });

            $('body').append($detailDialog);
        }

        function openDetailDialog(journalId) {
            journalId = parseInt(journalId, 10);

            if (!$detailDialog) {
                return;
            }

            resetDetailDialog();

            $detailDialog.show();
            $('body').addClass('VAS-gljpq-body-lock');

            if (isNaN(journalId) || journalId <= 0) {
                renderDetailError('Invalid journal id.');
                return;
            }

            loadJournalDetail(journalId);
        }

        function resetDetailDialog() {
            var id = $self.AD_UserHomeWidgetID;

            $detailDialog.find('#VAS-gljpq-dialog-title-' + id).text('—');
            $detailDialog.find('#VAS-gljpq-dialog-sub-' + id).text('—');

            if ($detailBody) {
                $detailBody.html('');
            }
        }

        function closeDetailDialog() {
            if (!$detailDialog) {
                return;
            }

            $detailDialog.hide();
            $('body').removeClass('VAS-gljpq-body-lock');
        }

        function showDetailBusy(show) {
            if (!$detailBusy) {
                return;
            }

            if (show) {
                $detailBusy.show();
            } else {
                $detailBusy.hide();
            }
        }

        function loadJournalDetail(journalId) {
            journalId = parseInt(journalId, 10);

            if (isNaN(journalId) || journalId <= 0) {
                renderDetailError('Invalid journal id.');
                return;
            }

            showDetailBusy(true);

            if ($detailBody) {
                $detailBody.html('');
            }

            $.ajax({
                url: baseUrl + 'VAS/VAS_041_GLJournalEntriesWidget/GetJournalEntryDetail',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: {
                    journalId: journalId
                },
                success: function (result) {
                    console.log('GetJournalEntryDetail raw result:', result);

                    var data = normalizeResponse(result);

                    console.log('GetJournalEntryDetail parsed data:', data);

                    if (!data) {
                        renderDetailError('No response from server.');
                        return;
                    }

                    if (data.error === true) {
                        renderDetailError(data.errorText || ('Journal details not found for ID: ' + journalId));
                        return;
                    }

                    if (data.error) {
                        renderDetailError(data.errorText || data.error || 'Error loading journal details.');
                        return;
                    }

                    if (!data.Journal && data.journal) {
                        data.Journal = data.journal;
                    }

                    if (!data.Lines && data.lines) {
                        data.Lines = data.lines;
                    }

                    if (!data.Lines && data.JournalLines) {
                        data.Lines = data.JournalLines;
                    }

                    if (!data.Lines && data.journalLines) {
                        data.Lines = data.journalLines;
                    }

                    if (!data.Journal) {
                        renderDetailError(lbl('VAS_045_JournalObjectNotReturned', 'Journal object was not returned from server.'));
                        return;
                    }

                    renderJournalDetail(data);
                },
                error: function (xhr) {
                    console.log('GetJournalEntryDetail ajax error:', xhr);
                    renderDetailError(xhr && xhr.responseText ? xhr.responseText : 'Error loading journal details.');
                },
                complete: function () {
                    showDetailBusy(false);
                }
            });
        }

        function renderJournalDetail(data) {
            var journal = data.Journal || data.journal || {};
            var lines = data.Lines || data.lines || data.JournalLines || data.journalLines || [];
            var precision = data.StdPrecision;

            if (precision === undefined || precision === null) {
                precision = data.stdPrecision;
            }

            var pillCls = PILL_CLASS[journal.DocStatus] || 'VAS-gljpq-pill-draft';
            var id = $self.AD_UserHomeWidgetID;

            var totalDebit = fmtAmt(journal.TotalDebit, precision);
            var totalCredit = fmtAmt(journal.TotalCredit, precision);

            var book = journal.AccountingBook || 'Primary';

            if (data.ISOCode) {
                book += ' · ' + data.ISOCode;
            }

            $detailDialog.find('#VAS-gljpq-dialog-title-' + id).text(
                (journal.DocumentNo || '') +
                (journal.Description ? ' · ' + journal.Description : '')
            );

            $detailDialog.find('#VAS-gljpq-dialog-sub-' + id).text(
                (journal.StatusName || journal.DocStatus || '') +
                (journal.DateAcct ? ' · ' + journal.DateAcct : '')
            );

            var html =
                '<div class="VAS-gljpq-detail-summary">' +

                '<div>' +
                '<span>' + esc(lbl('VAS_045_JournalNo', 'Journal No.')) + '</span>' +
                '<strong>' + esc(journal.DocumentNo) + '</strong>' +
                '</div>' +

                '<div>' +
                '<span>' + esc(lbl('VAS_045_Date', 'Date')) + '</span>' +
                '<strong>' + esc(journal.DateAcct) + '</strong>' +
                '</div>' +

                '<div>' +
                '<span>' + esc(lbl('VAS_045_Status', 'Status')) + '</span>' +
                '<strong>' +
                '<span class="VAS-gljpq-pill ' + pillCls + '">' +
                '<span></span>' +
                esc(journal.StatusName || journal.DocStatus) +
                '</span>' +
                '</strong>' +
                '</div>' +

                '<div>' +
                '<span>' + esc(lbl('VAS_045_AccountingBook', 'Accounting Book')) + '</span>' +
                '<strong>' + esc(book) + '</strong>' +
                '</div>' +

                '<div>' +
                '<span>' + esc(lbl('VAS_045_TotalDebit', 'Total Debit')) + '</span>' +
                '<strong>' + esc(totalDebit) + '</strong>' +
                '</div>' +

                '<div>' +
                '<span>' + esc(lbl('VAS_045_TotalCredit', 'Total Credit')) + '</span>' +
                '<strong>' + esc(totalCredit) + '</strong>' +
                '</div>' +

                '<div class="VAS-gljpq-detail-description">' +
                '<span>' + esc(lbl('VAS_045_Description', 'Description')) + '</span>' +
                '<strong>' + esc(journal.Description) + '</strong>' +
                '</div>' +

                '</div>' +

                '<div class="VAS-gljpq-detail-section-title">' + esc(lbl('VAS_045_JournalLines', 'Journal Lines')) + '</div>' +

                '<div class="VAS-gljpq-detail-lines-wrap">' +
                '<table class="VAS-gljpq-detail-lines">' +
                '<thead>' +
                '<tr>' +
                '<th>' + esc(lbl('VAS_045_Account', 'Account')) + '</th>' +
                '<th>' + esc(lbl('VAS_045_Debit', 'Debit')) + '</th>' +
                '<th>' + esc(lbl('VAS_045_Credit', 'Credit')) + '</th>' +
                '<th>' + esc(lbl('VAS_045_CostCenter', 'Cost Center')) + '</th>' +
                '<th>' + esc(lbl('VAS_045_BusinessPartner', 'Business Partner')) + '</th>' +
                '<th>' + esc(lbl('VAS_045_Product', 'Product')) + '</th>' +
                '<th>' + esc(lbl('VAS_045_Project', 'Project')) + '</th>' +
                '</tr>' +
                '</thead>' +
                '<tbody>';

            if (!lines || lines.length === 0) {
                html +=
                    '<tr>' +
                    '<td colspan="7" class="VAS-gljpq-detail-empty-line">' +
                    esc(lbl('VAS_045_NoJournalLinesReturned', 'No journal lines were returned from server.')) +
                    '</td>' +
                    '</tr>';
            } else {
                for (var i = 0; i < lines.length; i++) {
                    var line = lines[i];

                    var accountText = '';

                    if (line.AccountCode && line.AccountName) {
                        accountText = line.AccountCode + ' · ' + line.AccountName;
                    } else if (line.AccountCode) {
                        accountText = line.AccountCode;
                    } else if (line.AccountName) {
                        accountText = line.AccountName;
                    } else {
                        accountText = '-';
                    }

                    html +=
                        '<tr>' +
                        '<td>' + esc(accountText) + '</td>' +
                        '<td class="VAS-gljpq-detail-amt">' + esc(Number(line.Debit || 0) > 0 ? fmtAmt(line.Debit, precision) : '-') + '</td>' +
                        '<td class="VAS-gljpq-detail-amt">' + esc(Number(line.Credit || 0) > 0 ? fmtAmt(line.Credit, precision) : '-') + '</td>' +
                        '<td>' + esc(line.CostCenter || '-') + '</td>' +
                        '<td>' + esc(line.BPartner || '-') + '</td>' +
                        '<td>' + esc(line.Product || '-') + '</td>' +
                        '<td>' + esc(line.Project || '-') + '</td>' +
                        '</tr>';
                }
            }

            html +=
                '</tbody>' +
                '<tfoot>' +
                '<tr>' +
                '<td>' + esc(lbl('VAS_045_Total', 'Total')) + '</td>' +
                '<td class="VAS-gljpq-detail-amt">' + esc(totalDebit) + '</td>' +
                '<td class="VAS-gljpq-detail-amt">' + esc(totalCredit) + '</td>' +
                '<td colspan="4"></td>' +
                '</tr>' +
                '</tfoot>' +
                '</table>' +
                '</div>' +

                '<div class="VAS-gljpq-created-strip">' +
                '<span class="VAS-gljpq-avatar">' + esc(initials(journal.CreatedByName)) + '</span>' +
                '<div>' +
                '<span>' + esc(lbl('VAS_045_CreatedBy', 'Created By')) + '</span>' +
                '<strong>' + esc(journal.CreatedByName || '-') + '</strong>' +
                (journal.CreatedDate ? ' · drafted ' + esc(journal.CreatedDate) : '') +
                '</div>' +
                '</div>';

            if ($detailBody) {
                $detailBody.html(html);
            }
        }

        function renderDetailError(message) {
            var text = lbl('VIS_Error', 'Error loading data.');

            if (typeof message === 'string' && message.length > 0) {
                text = message;
            }

            if ($detailBody) {
                $detailBody.html(
                    '<div class="VAS-gljpq-dialog-empty">' +
                    esc(text) +
                    '</div>'
                );
            }
        }

        function initials(name) {
            var parts = String(name || '').trim().split(/\s+/);

            if (!parts.length || !parts[0]) {
                return '--';
            }

            if (parts.length === 1) {
                return parts[0].charAt(0).toUpperCase();
            }

            return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
        }

        function showError(message) {
            var id = $self.AD_UserHomeWidgetID;
            var text = lbl('VIS_Error', 'Error loading data.');

            if (typeof message === 'string' && message.length > 0) {
                text = message;
            }

            $root.find('#VAS-gljpq-body-' + id).html(
                '<div class="VAS-gljpq-empty">' +
                esc(text) +
                '</div>'
            );

            totalPages = 0;
            updatePager();
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (refreshTimer) {
                clearInterval(refreshTimer);
                refreshTimer = null;
            }

            if ($self && $self.AD_UserHomeWidgetID) {
                $(document).off('keydown.VAS-gljpq-' + $self.AD_UserHomeWidgetID);
            }

            $('body').removeClass('VAS-gljpq-body-lock');

            if ($detailDialog) {
                $detailDialog.remove();
                $detailDialog = null;
            }

            if ($root) {
                $root.off();
                $root.remove();
            }

            $detailBody = null;
            $detailBusy = null;
            currentData = null;
            $root = null;
            $self = null;
        };
    };

    VAS.VAS_045_GLJournalPendingWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_045_GLJournalPendingWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_045_GLJournalPendingWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);