/************************************************************
 * Module Name    : VAS
 * Purpose        : Payment Voucher tab panel
 * chronological  : Development
 * Created Date   : 4 May 2026
 * Created by     : VAI154
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_PaymentVoucherPanel = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth;

        var $self = this;
        var $root;
        var $busy;
        var $body;
        var $emptyState;
        var $errorState;
        var data = null;

        var STAGE_LABELS = [
            { code: "Drafted",   msgKey: "VAS_PVStageDrafted" },
            { code: "Submitted", msgKey: "VAS_PVStateSubmitted" },
            { code: "Reviewed",  msgKey: "VAS_PVStateReviewed" },
            { code: "Approved",  msgKey: "VAS_Approved" },
            { code: "Released",  msgKey: "VAS_PVStateReleased" }
        ];

        this.init = function () {
            $root = $('<section class="vas-pvp-root" role="region"></section>');
            $root.attr("aria-label", VIS.Msg.getMsg("VAS_PaymentVoucherOverview"));

            // Sticky panel header bar — design.md "Right Detail Panel Header Bar"
            // (56px, white surface, bottom border, title left, optional meta right).
            var $paneHeader = $(
                '<header class="vas-pvp-paneHeader">' +
                    '<div class="vas-pvp-paneTitle"></div>' +
                    '<div class="vas-pvp-paneMeta js-pane-meta"></div>' +
                '</header>'
            );
            $paneHeader.find(".vas-pvp-paneTitle")
                .text(VIS.Msg.getMsg("VAS_PaymentVoucherOverview"));
            $root.append($paneHeader);

            // Scrollable content region.
            var $scroll = $('<div class="vas-pvp-scroll"></div>');
            $body = $('<div class="vas-pvp-body"></div>');
            $emptyState = $('<div class="vas-pvp-empty" style="display:none;"></div>');
            $errorState = $('<div class="vas-pvp-empty" style="display:none;"></div>');
            $scroll.append($body).append($emptyState).append($errorState);
            $root.append($scroll);
            createBusyIndicator();
        };

        function createBusyIndicator() {
            $busy = $('<div class="vis-apanel-busy">' +
                      '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>' +
                      '</div>');
            $busy.css({
                "position": "absolute", "width": "100%", "height": "100%",
                "text-align": "center", "z-index": "999"
            });
            $busy[0].style.visibility = "hidden";
            $root.append($busy);
        }

        function showBusy(show) {
            if (!$busy || !$busy[0]) return;
            $busy[0].style.visibility = show ? "visible" : "hidden";
        }

        this.fetchData = function (recordID) {
            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_PaymentVoucherPanel/GetPaymentVoucher",
                type: "GET",
                dataType: "json",
                data: { C_Payment_ID: recordID },
                success: function (raw) {
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
                    render();
                    showBusy(false);
                },
                error: function (err) {
                    console.log(err);
                    data = null;
                    renderSectionError();
                    showBusy(false);
                }
            });
        };

        this.clear = function () {
            data = null;
            render();
        };

        function render() {
            $body.empty();
            $errorState.hide();

            // Update header bar's secondary meta — voucher number is the
            // canonical record identifier, mirroring "company name / record id"
            // per design.md right-panel meta rules.
            var $paneMeta = $root.find(".js-pane-meta").empty();

            if (!data || !data.C_Payment_ID) {
                $body.hide();
                $emptyState.empty();
                $emptyState.append($('<div class="vas-pvp-emptyTitle"></div>')
                    .text(VIS.Msg.getMsg("VAS_PVVoucherNotFound")));
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            if (data.VoucherNumber) {
                $paneMeta.text(data.VoucherNumber);
            }

            $body.append(buildIdentityRow());
            $body.append(buildSummaryCards());
            $body.append(buildApprovalCard());
            $body.append(buildAllocationsCard());
            $body.append(buildBottomGrid());
        }

        function renderSectionError() {
            $body.empty();
            $emptyState.hide();
            $errorState.empty();
            $errorState.append($('<div class="vas-pvp-emptyTitle"></div>')
                .text(VIS.Msg.getMsg("VAS_PVCouldntLoadSection")));
            var $retry = $('<button type="button" class="vas-pvp-retry"></button>')
                .text(VIS.Msg.getMsg("VAS_PVRetry"));
            $retry.on("click", function () {
                if ($self.record_ID) $self.fetchData($self.record_ID);
            });
            $errorState.append($retry).show();
        }

        // ---------- Identity strip — sits at the top of the scrollable content ----------
        // The panel title lives in the sticky pane header bar (per design.md).
        // This strip surfaces the record-level details that design.md says belong
        // "near the top": creation context, status, and quick action buttons.
        function buildIdentityRow() {
            var $hdr = $(
                '<div class="vas-pvp-identityRow">' +
                    '<div class="vas-pvp-identityLeft">' +
                        '<div class="vas-pvp-subline">' +
                            '<span class="js-created-prefix"></span>' +
                            '<span class="js-created-date"></span>' +
                            '<span class="vas-pvp-dot js-by-sep">·</span>' +
                            '<span class="js-by-prefix"></span>' +
                            '<span class="js-created-by"></span>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-pvp-headerRight">' +
                        '<div class="vas-pvp-statusPill js-status-pill" role="status">' +
                            '<span class="vas-pvp-statusDot"></span>' +
                            '<span class="js-status-text"></span>' +
                        '</div>' +
                        '<div class="vas-pvp-iconBtns">' +
                            '<button type="button" class="vas-pvp-iconBtn js-act-email"   aria-label="" title="">' +
                                '<i class="fa fa-envelope" aria-hidden="true"></i>' +
                            '</button>' +
                            '<button type="button" class="vas-pvp-iconBtn js-act-pdf"     aria-label="" title="">' +
                                '<i class="fa fa-download" aria-hidden="true"></i>' +
                            '</button>' +
                            '<button type="button" class="vas-pvp-iconBtn js-act-more"    aria-label="" title="">' +
                                '<i class="fa fa-ellipsis-h" aria-hidden="true"></i>' +
                            '</button>' +
                        '</div>' +
                    '</div>' +
                '</div>'
            );

            $hdr.find(".js-created-prefix").text(VIS.Msg.getMsg("Created") + " ");
            $hdr.find(".js-created-date").text(formatFullDate(data.CreatedAt));
            if (data.CreatedByName) {
                $hdr.find(".js-by-prefix").text(VIS.Msg.getMsg("VAS_PVBy") + " ");
                $hdr.find(".js-created-by").text(data.CreatedByName);
            } else {
                $hdr.find(".js-by-sep").hide();
                $hdr.find(".js-by-prefix").hide();
            }

            // Status pill — color tone reflects the resolved UI status.
            var $pill = $hdr.find(".js-status-pill");
            var statusText = (data.UiSubStatus || "")
                ? (resolveStatusLabel(data.UiStatus) + " · " + data.UiSubStatus)
                : resolveStatusLabel(data.UiStatus);
            $pill.find(".js-status-text").text(statusText);
            $pill.attr("data-tone", data.UiStatus || "draft");

            // Icon buttons — accessible labels and stub click handlers.
            var emailLabel = VIS.Msg.getMsg("VAS_PVEmailAction");
            var pdfLabel   = VIS.Msg.getMsg("VAS_DownloadPDF");
            var moreLabel  = VIS.Msg.getMsg("VAS_PVMoreAction");

            $hdr.find(".js-act-email").attr({ "aria-label": emailLabel, "title": emailLabel });
            $hdr.find(".js-act-pdf").attr({ "aria-label": pdfLabel,   "title": pdfLabel });
            $hdr.find(".js-act-more").attr({ "aria-label": moreLabel,  "title": moreLabel });

            // TODO: wire icon buttons to real routes/handlers when available.
            $hdr.find(".vas-pvp-iconBtn").on("click", function () {
                var info = (VIS && VIS.ADialog && VIS.ADialog.info)
                    ? VIS.ADialog.info : function (m) { console.log(m); };
                info(VIS.Msg.getMsg("VAS_PVActionStub"));
            });

            return $hdr;
        }

        function resolveStatusLabel(uiStatus) {
            switch (uiStatus) {
                case "released":  return VIS.Msg.getMsg("VAS_PVStateReleased");
                case "approved":  return VIS.Msg.getMsg("VAS_Approved");
                case "submitted": return VIS.Msg.getMsg("VAS_PVStateSubmitted");
                case "reviewed":  return VIS.Msg.getMsg("VAS_PVStateReviewed");
                case "draft":
                default:          return VIS.Msg.getMsg("VAS_Draft");
            }
        }

        // ---------- Section 4.3 — summary metric cards ----------
        function buildSummaryCards() {
            var $grid = $('<div class="vas-pvp-summaryGrid"></div>');

            // Cell 1 — Payee
            var $payee = $('<div class="vas-pvp-card vas-pvp-summaryCell vas-pvp-cell-payee"></div>');
            $payee.append($('<div class="vas-pvp-cellLabel"></div>').text(VIS.Msg.getMsg("VAS_PVPayee")));
            $payee.append($('<div class="vas-pvp-payeeName"></div>').text(data.PayeeName || ""));
            var bankBits = [];
            if (data.PayeeBankName) bankBits.push(data.PayeeBankName);
            if (data.PayeeAccountNoLast4) bankBits.push("••• " + data.PayeeAccountNoLast4);
            if (bankBits.length) {
                $payee.append($('<div class="vas-pvp-bankLine"></div>')
                    .text(VIS.Msg.getMsg("VAS_PVBank") + " · " + bankBits.join(" · ")));
            }
            if (typeof data.YtdPaidAmount === "number" && data.YtdPaidAmount > 0) {
                var ytdText = VIS.Msg.getMsg("VAS_PVYtdPaid")
                    .replace("{0}", formatAmount(data.YtdPaidAmount, data.CurSymbol, data.ISO_Code, data.StdPrecision));
                $payee.append($('<div class="vas-pvp-ytdPill"></div>').text(ytdText));
            }

            // Cell 2 — Amount
            var $amt = $('<div class="vas-pvp-card vas-pvp-summaryCell vas-pvp-cell-amount"></div>');
            $amt.append($('<div class="vas-pvp-cellLabel"></div>').text(VIS.Msg.getMsg("Amount")));
            $amt.append($('<div class="vas-pvp-amountValue"></div>').text(formatAmount(
                +data.Amount || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
            var amtSubBits = [];
            if (data.ISO_Code) amtSubBits.push(data.ISO_Code);
            if (data.PaymentMethodName) amtSubBits.push(data.PaymentMethodName);
            if (amtSubBits.length) {
                $amt.append($('<div class="vas-pvp-amountSub"></div>').text(amtSubBits.join(" · ")));
            }

            // Cell 3 — Payment date
            var $date = $('<div class="vas-pvp-card vas-pvp-summaryCell vas-pvp-cell-date"></div>');
            $date.append($('<div class="vas-pvp-cellLabel"></div>').text(VIS.Msg.getMsg("VAS_PaymentDate")));
            $date.append($('<div class="vas-pvp-dateValue"></div>').text(formatFullDate(data.ScheduledPaymentDate)));
            $date.append(buildScheduleSubLine());

            // Cell 4 — Approval
            var $appr = $('<div class="vas-pvp-card vas-pvp-summaryCell vas-pvp-cell-approval"></div>');
            $appr.append($('<div class="vas-pvp-cellLabel"></div>').text(VIS.Msg.getMsg("VAS_PVApproval")));
            var apprText = VIS.Msg.getMsg("VAS_PVApproversOf")
                .replace("{0}", +data.ApprovedCount || 0)
                .replace("{1}", +data.ApproverCount || 0);
            $appr.append($('<div class="vas-pvp-apprValue"></div>').text(apprText));
            if (data.FinalApproverName) {
                var finalText = VIS.Msg.getMsg("VAS_PVFinalBy")
                    .replace("{0}", data.FinalApproverName)
                    .replace("{1}", data.FinalApproverTitle || "");
                $appr.append($('<div class="vas-pvp-apprFinal"></div>').text("✓ " + finalText));
            }

            $grid.append($payee).append($amt).append($date).append($appr);
            return $grid;
        }

        function buildScheduleSubLine() {
            var $sub = $('<div class="vas-pvp-dateSub"></div>');
            if (data.UiStatus === "released") {
                $sub.text(VIS.Msg.getMsg("VAS_Paid")).addClass("tone-paid");
                return $sub;
            }
            if (!data.ScheduledPaymentDate) return $sub;

            var d = new Date(data.ScheduledPaymentDate);
            if (isNaN(d.getTime())) return $sub;

            var today = new Date();
            today.setHours(0, 0, 0, 0);
            d.setHours(0, 0, 0, 0);
            var msPerDay = 24 * 60 * 60 * 1000;
            var diff = Math.round((d.getTime() - today.getTime()) / msPerDay);

            if (diff >= 0) {
                $sub.text(VIS.Msg.getMsg("VAS_PVScheduledIn").replace("{0}", diff))
                    .addClass("tone-scheduled");
            } else {
                $sub.text(VIS.Msg.getMsg("VAS_PVOverdue")).addClass("tone-overdue");
            }
            return $sub;
        }

        // ---------- Section 4.4 — approval workflow ----------
        function buildApprovalCard() {
            var $card = $('<div class="vas-pvp-card vas-pvp-apprCard"></div>');

            // Stage tracker — fixed 5-column lifecycle.
            var stageState = computeStageState();
            var $tracker = $('<div class="vas-pvp-stageTracker"></div>');
            for (var i = 0; i < STAGE_LABELS.length; i++) {
                var stage = STAGE_LABELS[i];
                var state = stageState[stage.code] || "pending";
                var $cell = $('<div class="vas-pvp-stageCell"></div>')
                    .attr("data-state", state);
                $cell.append($('<div class="vas-pvp-stageLabel"></div>')
                    .text(VIS.Msg.getMsg(stage.msgKey)));
                var valueText;
                if (state === "complete")      valueText = "✓";
                else if (state === "active")   valueText = VIS.Msg.getMsg("Active");
                else                           valueText = VIS.Msg.getMsg("Pending");
                $cell.append($('<div class="vas-pvp-stageValue"></div>').text(valueText));
                $tracker.append($cell);
            }
            $card.append($tracker);

            // Approval log rows — only render completed/active steps that have a label.
            var steps = (data && data.WorkflowSteps) || [];
            var $log = $('<div class="vas-pvp-apprLog"></div>');
            var rendered = 0;
            for (var k = 0; k < steps.length; k++) {
                var s = steps[k];
                if (s.Status === "pending") continue;
                if (!s.Label && !s.RawLabel) continue;
                $log.append(buildApprovalLogRow(s));
                rendered++;
            }
            if (rendered > 0) $card.append($log);

            // Release queue row — shown only when status === approved AND the
            // user has the resolved release permission.
            if (data.UiStatus === "approved" && data.ReleaseQueue) {
                $card.append(buildReleaseQueueRow());
            }

            return $card;
        }

        function computeStageState() {
            var state = { Drafted: "pending", Submitted: "pending", Reviewed: "pending",
                          Approved: "pending", Released: "pending" };

            // Walk the server-shaped workflow steps; map each to the canonical lifecycle.
            var steps = (data && data.WorkflowSteps) || [];
            for (var i = 0; i < steps.length; i++) {
                var s = steps[i];
                if (state[s.Label] === undefined) continue;
                if (s.Status === "complete" || state[s.Label] === "pending")
                    state[s.Label] = s.Status;
            }

            // Derive missing stages from coarse signals on the voucher itself.
            if (state.Drafted === "pending") state.Drafted = "complete";
            if (data.UiStatus === "released") {
                state.Submitted = "complete";
                state.Reviewed  = "complete";
                state.Approved  = "complete";
                state.Released  = "complete";
            } else if (data.UiStatus === "approved") {
                state.Submitted = "complete";
                state.Reviewed  = "complete";
                state.Approved  = "complete";
                if (state.Released !== "complete") state.Released = "active";
            } else if (data.UiStatus === "submitted") {
                if (state.Submitted === "pending") state.Submitted = "complete";
                if (state.Reviewed === "pending")  state.Reviewed  = "active";
            } else if (data.UiStatus === "draft") {
                if (state.Submitted === "pending") state.Submitted = "active";
            }

            // Ensure exactly one "active" stage — the first non-complete one.
            var active = false;
            for (var j = 0; j < STAGE_LABELS.length; j++) {
                var code = STAGE_LABELS[j].code;
                if (state[code] === "complete") continue;
                if (!active && state[code] === "active") { active = true; continue; }
                if (!active) { state[code] = "active"; active = true; }
                else state[code] = "pending";
            }
            return state;
        }

        function buildApprovalLogRow(step) {
            var $row = $('<div class="vas-pvp-apprRow"></div>');
            var $role = $('<div class="vas-pvp-apprRole"></div>')
                .text(localizeStageLabel(step.Label) || step.RawLabel || "");
            var $actor = $('<div class="vas-pvp-apprActor"></div>');
            if (step.ActorName) $actor.append($('<span></span>').text(step.ActorName));
            if (step.ActorRoleTitle) {
                $actor.append($('<span class="vas-pvp-apprDot"></span>').text(" · "));
                $actor.append($('<span class="vas-pvp-apprActorRole"></span>').text(step.ActorRoleTitle));
            }
            var $when = $('<div class="vas-pvp-apprWhen"></div>')
                .text(formatTimestamp(step.Timestamp));
            $row.append($role).append($actor).append($when);
            return $row;
        }

        function localizeStageLabel(canonical) {
            for (var i = 0; i < STAGE_LABELS.length; i++) {
                if (STAGE_LABELS[i].code === canonical)
                    return VIS.Msg.getMsg(STAGE_LABELS[i].msgKey);
            }
            return canonical;
        }

        function buildReleaseQueueRow() {
            var queue = data.ReleaseQueue || {};
            var queueLabel = queue.QueueName || VIS.Msg.getMsg("VAS_PVTreasuryQueue");
            var pending = queue.PendingArtifactDescription || "";
            var waiting = +queue.WaitingDays || 0;

            var $row = $('<div class="vas-pvp-releaseRow"></div>');
            $row.append($('<div class="vas-pvp-releaseRole"></div>')
                .text(VIS.Msg.getMsg("VAS_PVAwaitingRelease")));

            var subText = queueLabel;
            if (pending) subText += " · " + pending;
            if (waiting > 0) subText += " · " + VIS.Msg.getMsg("VAS_PVWaitingDays").replace("{0}", waiting);
            $row.append($('<div class="vas-pvp-releaseQueue"></div>').text(subText));

            // Right column — Release now button (permission-gated).
            var $right = $('<div class="vas-pvp-releaseRight"></div>');
            if (data.CanRelease) {
                var $btn = $('<button type="button" class="vas-pvp-releaseBtn"></button>')
                    .text(VIS.Msg.getMsg("VAS_PVReleaseNow"))
                    .attr("aria-label", VIS.Msg.getMsg("VAS_PVReleaseAriaLabel")
                        .replace("{0}", data.VoucherNumber || ""));
                $btn.on("click", onReleaseClick);
                $right.append($btn);
                var $err = $('<div class="vas-pvp-releaseErr" style="display:none;"></div>');
                $right.append($err);
            }
            $row.append($right);
            return $row;
        }

        function onReleaseClick(e) {
            e.preventDefault();
            var $btn = $(this);
            if ($btn.is(":disabled")) return;
            var $row = $btn.closest(".vas-pvp-releaseRow");
            var $err = $row.find(".vas-pvp-releaseErr");
            $err.hide().text("");

            $btn.prop("disabled", true);
            $btn.addClass("is-loading");
            $btn.text(VIS.Msg.getMsg("VAS_PVReleasing"));

            $.ajax({
                url: VIS.Application.contextUrl + "VAS_PaymentVoucherPanel/ReleaseNow",
                type: "POST",
                dataType: "json",
                data: { C_Payment_ID: $self.record_ID },
                success: function (raw) {
                    var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (resp && resp.Success) {
                        $self.fetchData($self.record_ID);
                    } else {
                        $btn.prop("disabled", false).removeClass("is-loading")
                            .text(VIS.Msg.getMsg("VAS_PVReleaseNow"));
                        $err.text((resp && resp.Message)
                            ? resp.Message
                            : VIS.Msg.getMsg("VAS_PVReleaseFailed")).show();
                    }
                },
                error: function () {
                    $btn.prop("disabled", false).removeClass("is-loading")
                        .text(VIS.Msg.getMsg("VAS_PVReleaseNow"));
                    $err.text(VIS.Msg.getMsg("VAS_PVReleaseFailed")).show();
                }
            });
        }

        // ---------- Section 4.5 — allocated invoices ----------
        function buildAllocationsCard() {
            var $card = $('<div class="vas-pvp-card vas-pvp-allocCard"></div>');

            var $header = $('<div class="vas-pvp-allocHeader"></div>');
            $header.append($('<div class="vas-pvp-allocTitle"></div>')
                .text(VIS.Msg.getMsg("VAS_PVAllocatedInvoices")));
            $header.append(buildMatchPill());
            $card.append($header);

            // Column headers.
            var $cols = $('<div class="vas-pvp-allocCols"></div>');
            $cols.append($('<div></div>').text(VIS.Msg.getMsg("Invoice")));
            $cols.append($('<div></div>').text(VIS.Msg.getMsg("Date")));
            $cols.append($('<div class="is-num"></div>').text(VIS.Msg.getMsg("Amount")));
            $cols.append($('<div class="is-num"></div>').text(VIS.Msg.getMsg("VAS_PVColAllocated")));
            $cols.append($('<div class="is-num"></div>').text(VIS.Msg.getMsg("VAS_PVColBalance")));
            $cols.append($('<div></div>').text(VIS.Msg.getMsg("Status")));
            $card.append($cols);

            var rows = (data && data.Allocations) || [];
            if (!rows.length) {
                $card.append($('<div class="vas-pvp-allocEmpty"></div>')
                    .text(VIS.Msg.getMsg("VAS_PVNoInvoices")));
                return $card;
            }
            for (var i = 0; i < rows.length; i++) {
                $card.append(buildAllocationRow(rows[i]));
            }

            // Footer totals.
            var $totals = $('<div class="vas-pvp-allocTotals"></div>');
            $totals.append($('<div class="vas-pvp-allocTotalsLabel"></div>')
                .text(VIS.Msg.getMsg("VAS_PVTotalAllocated")));
            $totals.append($('<div class="vas-pvp-allocTotalsValue"></div>')
                .text(formatAmount(+data.AllocatedTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)
                    + " / " +
                    formatAmount(+data.Amount || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision)));
            $card.append($totals);

            return $card;
        }

        function buildMatchPill() {
            var $pill = $('<div class="vas-pvp-matchPill"></div>');
            var matchStatus = data.MatchStatus || "unmatched";
            $pill.attr("data-state", matchStatus);

            var label;
            var allocFmt = formatAmount(+data.AllocatedTotal || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision);
            var totalFmt = formatAmount(+data.Amount || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision);
            switch (matchStatus) {
                case "fully_matched":
                    label = VIS.Msg.getMsg("VAS_PVFullyMatched") + " · " + totalFmt + " of " + totalFmt;
                    $pill.append('<i class="fa fa-check vas-pvp-matchIcon" aria-hidden="true"></i>');
                    break;
                case "partially_matched":
                    label = VIS.Msg.getMsg("VAS_PVPartiallyMatched") + " · " + allocFmt + " of " + totalFmt;
                    break;
                default:
                    label = VIS.Msg.getMsg("VAS_PVUnmatched");
                    break;
            }
            $pill.append($('<span></span>').text(label));
            return $pill;
        }

        function buildAllocationRow(row) {
            var ariaText = (row.InvoiceNumber || "") + " · " +
                           (row.Description || "") + " · " +
                           formatAmount(+row.AllocatedAmount || 0,
                                        data.CurSymbol, data.ISO_Code, data.StdPrecision);

            var $row = $('<button type="button" class="vas-pvp-allocRow"></button>')
                .attr("aria-label", ariaText);

            var $invCell = $('<div class="vas-pvp-allocInvCell"></div>');
            $invCell.append($('<div class="vas-pvp-allocInvNo"></div>').text(row.InvoiceNumber || ""));
            if (row.Description) {
                $invCell.append($('<div class="vas-pvp-allocInvDesc"></div>').text(row.Description));
            }
            $row.append($invCell);

            $row.append($('<div class="vas-pvp-allocDate"></div>').text(formatFullDate(row.InvoiceDate)));
            $row.append($('<div class="vas-pvp-allocAmt is-num"></div>')
                .text(formatAmount(+row.InvoiceTotalAmount || 0,
                                   data.CurSymbol, data.ISO_Code, data.StdPrecision)));
            $row.append($('<div class="vas-pvp-allocAlloc is-num"></div>')
                .text(formatAmount(+row.AllocatedAmount || 0,
                                   data.CurSymbol, data.ISO_Code, data.StdPrecision)));

            var bal = +row.RemainingBalance || 0;
            var $bal = $('<div class="vas-pvp-allocBal is-num"></div>')
                .text(formatAmount(bal, data.CurSymbol, data.ISO_Code, data.StdPrecision));
            if (bal === 0)      $bal.addClass("tone-zero");
            else if (bal > 0)   $bal.addClass("tone-remaining");
            else                $bal.addClass("tone-over");
            $row.append($bal);

            var $st = $('<div class="vas-pvp-allocStatusCell"></div>');
            var $stPill = $('<span class="vas-pvp-allocStatusPill"></span>')
                .attr("data-state", row.SettlementStatus || "open");
            $stPill.text(localizeSettlement(row.SettlementStatus));
            $st.append($stPill);
            $row.append($st);

            // TODO: route to invoice detail when navigation route exists.
            $row.on("click", function () {
                var info = (VIS && VIS.ADialog && VIS.ADialog.info)
                    ? VIS.ADialog.info : function (m) { console.log(m); };
                info(VIS.Msg.getMsg("VAS_PVActionStub"));
            });
            return $row;
        }

        function localizeSettlement(s) {
            switch (s) {
                case "closed":  return VIS.Msg.getMsg("VAS_Closed");
                case "partial": return VIS.Msg.getMsg("VAS_Partial");
                case "open":
                default:        return VIS.Msg.getMsg("Open");
            }
        }

        // ---------- Section 4.6 — bottom 2-col grid (notes + activity) ----------
        function buildBottomGrid() {
            var $grid = $('<div class="vas-pvp-bottomGrid"></div>');
            $grid.append(buildNotesCard());
            $grid.append(buildActivityCard());
            return $grid;
        }

        function buildNotesCard() {
            var $card = $('<div class="vas-pvp-card vas-pvp-notesCard"></div>');

            var $header = $('<div class="vas-pvp-notesHeader"></div>');
            $header.append($('<div class="vas-pvp-notesTitle"></div>')
                .text(VIS.Msg.getMsg("VAS_PVPaymentNotes")));
            if (data.CanEditNotes && data.UiStatus !== "released") {
                var $edit = $('<button type="button" class="vas-pvp-notesEdit"></button>')
                    .text(VIS.Msg.getMsg("Edit"));
                $edit.on("click", function () {
                    // TODO: open inline edit / route to voucher edit per existing module pattern.
                    var info = (VIS && VIS.ADialog && VIS.ADialog.info)
                        ? VIS.ADialog.info : function (m) { console.log(m); };
                    info(VIS.Msg.getMsg("VAS_PVActionStub"));
                });
                $header.append($edit);
            }
            $card.append($header);

            var notes = data.PaymentNotes || "";
            var $body = $('<div class="vas-pvp-notesBody"></div>');
            if (notes) {
                $body.text(notes);
            } else {
                $body.text(VIS.Msg.getMsg("VAS_PVNoNotes")).addClass("is-empty");
            }
            $card.append($body);

            // Metadata grid (Terms / GL posting / Cost center / Reference).
            var $meta = $('<div class="vas-pvp-notesMeta"></div>');
            $meta.append(buildMetaField("VAS_PVTerms",      data.PaymentTerms));
            $meta.append(buildMetaField("VAS_PVGLPosting",  resolveGlPosting(data.GLPostingStatus)));
            $meta.append(buildMetaField("VAS_PVCostCenter", buildCostCenterValue()));
            $meta.append(buildMetaField("VAS_PVReference",  data.PurchaseOrderReference));
            $card.append($meta);

            return $card;
        }

        function buildMetaField(msgKey, value) {
            var $field = $('<div class="vas-pvp-metaField"></div>');
            $field.append($('<div class="vas-pvp-metaLabel"></div>').text(VIS.Msg.getMsg(msgKey)));
            $field.append($('<div class="vas-pvp-metaValue"></div>').text(value || "—"));
            return $field;
        }

        function resolveGlPosting(code) {
            if (!code) return "";
            if (code === "Y") return VIS.Msg.getMsg("VAS_PVPostedYes");
            if (code === "N") return VIS.Msg.getMsg("VAS_PVPostedNo");
            return code;
        }

        function buildCostCenterValue() {
            var bits = [];
            if (data.CostCenterCode) bits.push(data.CostCenterCode);
            if (data.CostCenterName) bits.push(data.CostCenterName);
            return bits.join(" · ");
        }

        function buildActivityCard() {
            var $card = $('<div class="vas-pvp-card vas-pvp-activityCard"></div>');
            var events = (data && data.Activity) || [];

            var $header = $('<div class="vas-pvp-activityHeader"></div>');
            $header.append($('<div class="vas-pvp-activityTitle"></div>')
                .text(VIS.Msg.getMsg("VAS_PVRecentActivity")));
            $header.append($('<div class="vas-pvp-activityCount"></div>')
                .text(VIS.Msg.getMsg("VAS_PVEvents").replace("{0}", events.length)));
            $card.append($header);

            if (!events.length) {
                $card.append($('<div class="vas-pvp-activityEmpty"></div>')
                    .text(VIS.Msg.getMsg("VAS_PVNoActivity")));
                return $card;
            }

            for (var i = 0; i < events.length; i++) {
                $card.append(buildActivityRow(events[i]));
            }
            return $card;
        }

        function buildActivityRow(ev) {
            var $row = $('<div class="vas-pvp-actRow"></div>');
            var iconConfig = activityIconForType(ev.EventType);
            var $well = $('<div class="vas-pvp-actIconWell"></div>')
                .attr("data-tone", iconConfig.tone);
            $well.append('<i class="fa ' + iconConfig.fa + '" aria-hidden="true"></i>');
            $row.append($well);

            var $desc = $('<div class="vas-pvp-actDesc"></div>');
            // Bold the action verb only.
            var verb = activityVerb(ev.EventType);
            $desc.append($('<strong></strong>').text(verb));
            if (ev.ActorName) {
                $desc.append(document.createTextNode(" " + VIS.Msg.getMsg("VAS_PVBy") + " "));
                $desc.append($('<span></span>').text(ev.ActorName));
            }
            $row.append($desc);

            $row.append($('<div class="vas-pvp-actWhen"></div>')
                .text(formatTimestamp(ev.Timestamp)));
            return $row;
        }

        function activityIconForType(t) {
            switch (t) {
                case "approved":
                case "released":           return { fa: "fa-check",       tone: "blue" };
                case "rejected":           return { fa: "fa-times",       tone: "red" };
                case "created":            return { fa: "fa-plus",        tone: "neutral" };
                case "reviewed":
                case "note_edited":
                case "allocation_changed":
                default:                   return { fa: "fa-align-left",  tone: "neutral" };
            }
        }

        function activityVerb(t) {
            switch (t) {
                case "approved":           return VIS.Msg.getMsg("VAS_Approved");
                case "released":           return VIS.Msg.getMsg("VAS_PVStateReleased");
                case "rejected":           return VIS.Msg.getMsg("VAS_PVActRejected");
                case "created":            return VIS.Msg.getMsg("Created");
                case "reviewed":           return VIS.Msg.getMsg("VAS_PVStateReviewed");
                case "note_edited":        return VIS.Msg.getMsg("VAS_PVActNoteEdited");
                case "allocation_changed": return VIS.Msg.getMsg("VAS_PVActAllocationRevised");
                default:                   return VIS.Msg.getMsg("VAS_PVStateReviewed");
            }
        }

        // ---------- Formatters ----------
        function formatFullDate(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                return new Intl.DateTimeFormat(window.navigator.language, {
                    day: "2-digit", month: "short", year: "numeric"
                }).format(d);
            } catch (e) {
                return d.toDateString();
            }
        }

        function formatTimestamp(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                var datePart = new Intl.DateTimeFormat(window.navigator.language, {
                    day: "2-digit", month: "short"
                }).format(d);
                var timePart = new Intl.DateTimeFormat(window.navigator.language, {
                    hour: "numeric", minute: "2-digit"
                }).format(d);
                return datePart + " · " + timePart;
            } catch (e) {
                return d.toString();
            }
        }

        function formatAmount(value, symbol, iso, precision) {
            var p = (precision >= 0) ? precision : 2;
            try {
                if (iso) {
                    return new Intl.NumberFormat(window.navigator.language, {
                        style: "currency", currency: iso,
                        minimumFractionDigits: p, maximumFractionDigits: p
                    }).format(value || 0);
                }
            } catch (e) { /* fall through */ }

            // Fallback when ISO code is missing or unsupported by the runtime.
            var sign = value < 0 ? "-" : "";
            var abs  = Math.abs(value || 0);
            var formatted = abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p, maximumFractionDigits: p
            });
            var cur = symbol || iso || "";
            return sign + (cur ? cur + " " : "") + formatted;
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_PaymentVoucherPanel.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    VAS.VAS_PaymentVoucherPanel.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    VAS.VAS_PaymentVoucherPanel.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    VAS.VAS_PaymentVoucherPanel.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
