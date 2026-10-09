/************************************************************
 * Module Name    : VAS
 * Purpose        : Invoice Overview tab panel
 * chronological  : Development
 * Created Date   : 30 April 2026
 * Created by     : VAI154
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_InvoiceOverview = function () {
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
        var data = null;

        this.init = function () {
            $root = $('<div class="vas-invovw-root"></div>');
            $body = $('<div class="vas-invovw-body"></div>');
            $emptyState = $('<div class="vas-invovw-empty" style="display:none;"></div>');
            $emptyState.text(VIS.Msg.getMsg("VAS_NoData"));
            $root.append($body).append($emptyState);
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
                url: VIS.Application.contextUrl + "VAS_InvoiceOverview/GetInvoiceOverview",
                type: "GET",
                dataType: "json",
                data: { C_Invoice_ID: recordID },
                success: function (raw) {
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = parsed;
                    render();
                    showBusy(false);
                },
                error: function (err) {
                    console.log(err);
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

            if (!data || !data.C_Invoice_ID) {
                $body.hide();
                $emptyState.show();
                return;
            }

            $emptyState.hide();
            $body.show();

            var $card = $(
                '<div class="vas-invovw-card">' +
                    '<div class="vas-invovw-headerRow">' +
                        '<div class="vas-invovw-headerLeft">' +
                            '<div class="vas-invovw-title"></div>' +
                            '<div class="vas-invovw-subline">' +
                                '<span class="js-doc-no"></span>' +
                                '<span class="vas-invovw-sublineDot">·</span>' +
                                '<span class="js-doc-date"></span>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-invovw-headerRight">' +
                            '<div class="vas-invovw-bpName js-bp-name"></div>' +
                            '<div class="vas-invovw-openChip">' +
                                '<span class="js-open-count"></span>' +
                                '<span class="js-open-label"></span>' +
                            '</div>' +
                        '</div>' +
                    '</div>' +

                    '<div class="vas-invovw-actionsRow">' +
                        '<button type="button" class="vas-invovw-btn is-primary"   data-action="record-payment"></button>' +
                        '<button type="button" class="vas-invovw-btn is-secondary" data-action="send-reminder"></button>' +
                        '<button type="button" class="vas-invovw-btn is-ghost"     data-action="download-pdf"></button>' +
                        '<button type="button" class="vas-invovw-btn is-ghost"     data-action="duplicate"></button>' +
                        '<button type="button" class="vas-invovw-btn is-outline"   data-action="view-ledger"></button>' +
                    '</div>' +

                    '<div class="vas-invovw-detailRow">' +
                        '<div class="vas-invovw-detailCol tone-customer">' +
                            '<div class="vas-invovw-detailLabel js-cust-label"></div>' +
                            '<div class="vas-invovw-detailValue js-cust-name"></div>' +
                            '<div class="vas-invovw-detailMeta">' +
                                '<span class="js-contact-name"></span>' +
                                '<span class="vas-invovw-detailDot js-contact-sep">·</span>' +
                                '<span class="js-contact-title"></span>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-invovw-detailCol tone-due">' +
                            '<div class="vas-invovw-detailLabel js-due-label"></div>' +
                            '<div class="vas-invovw-detailValue js-due-date"></div>' +
                            '<div class="vas-invovw-detailMeta">' +
                                '<span class="vas-invovw-duePill js-due-pill"></span>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-invovw-detailCol tone-outstanding">' +
                            '<div class="vas-invovw-detailLabel js-out-label"></div>' +
                            '<div class="vas-invovw-detailValue js-out-amt"></div>' +
                            '<div class="vas-invovw-detailMeta js-out-meta"></div>' +
                        '</div>' +
                    '</div>' +

                    '<div class="vas-invovw-timelineRow js-timeline"></div>' +

                    '<div class="vas-invovw-eligibleSection js-eligible-section">' +
                        '<div class="vas-invovw-eligibleLeft">' +
                            '<div class="vas-invovw-eligibleIcon">' +
                                '<i class="fa fa-refresh" aria-hidden="true"></i>' +
                            '</div>' +
                            '<div class="vas-invovw-eligibleText">' +
                                '<div class="vas-invovw-eligibleTitle js-eligible-title"></div>' +
                                '<div class="vas-invovw-eligibleSubtitle js-eligible-sub"></div>' +
                            '</div>' +
                        '</div>' +
                        '<button type="button" class="vas-invovw-btn is-primary js-eligible-btn"></button>' +
                    '</div>' +

                    '<div class="vas-invovw-linesSection js-lines-section">' +
                        '<div class="vas-invovw-linesHeader js-lines-title"></div>' +
                        '<div class="vas-invovw-linesTableWrap">' +
                            '<table class="vas-invovw-linesTable">' +
                                '<thead><tr class="js-lines-thead"></tr></thead>' +
                                '<tbody class="js-lines-body"></tbody>' +
                            '</table>' +
                        '</div>' +
                        '<div class="vas-invovw-linesTotals js-lines-totals"></div>' +
                    '</div>' +

                    '<div class="vas-invovw-riskSection js-risk-section">' +
                        '<div class="vas-invovw-riskLeft">' +
                            '<div class="vas-invovw-riskIcon">' +
                                '<i class="fa fa-arrow-up" aria-hidden="true"></i>' +
                            '</div>' +
                            '<div class="vas-invovw-riskText">' +
                                '<div class="vas-invovw-riskTitle js-risk-title"></div>' +
                                '<div class="vas-invovw-riskSub js-risk-sub"></div>' +
                            '</div>' +
                        '</div>' +
                        '<div class="vas-invovw-riskBadge js-risk-badge"></div>' +
                    '</div>' +

                    '<div class="vas-invovw-notesSection js-notes-section">' +
                        '<div class="vas-invovw-notesHeader">' +
                            '<div class="vas-invovw-notesHeaderText">' +
                                '<div class="vas-invovw-notesTitle js-notes-title"></div>' +
                                '<div class="vas-invovw-notesMeta js-notes-meta"></div>' +
                            '</div>' +
                            '<button type="button" class="vas-invovw-btn is-secondary js-notes-add"></button>' +
                        '</div>' +
                        '<div class="vas-invovw-notesList js-notes-list"></div>' +
                        '<div class="vas-invovw-notesEmpty js-notes-empty" style="display:none;"></div>' +
                    '</div>' +
                '</div>'
            );

            $card.find(".vas-invovw-title").text(VIS.Msg.getMsg("VAS_InvoiceOverview"));
            $card.find(".js-doc-no").text(data.DocumentNo || "");
            var issuedDate = formatDate(data.DateInvoiced);
            if (issuedDate) {
                $card.find(".js-doc-date").text(VIS.Msg.getMsg("VAS_Issued") + " " + issuedDate);
            } else {
                $card.find(".js-doc-date").hide();
                $card.find(".vas-invovw-sublineDot").hide();
            }
            if (!data.DocumentNo) {
                $card.find(".js-doc-no").hide();
                $card.find(".vas-invovw-sublineDot").hide();
            }

            $card.find(".js-bp-name").text(data.BPName || "");
            $card.find(".js-open-count").text(data.OpenInvoiceCount || 0);
            $card.find(".js-open-label").text(VIS.Msg.getMsg("VAS_CustomerOpenInvoices"));

            renderActions($card.find(".vas-invovw-actionsRow"));

            $card.find(".js-cust-label").text(VIS.Msg.getMsg("Customer"));
            $card.find(".js-cust-name").text(data.BPName || "");

            var contact = data.ContactName || "";
            var title   = data.ContactTitle || "";
            $card.find(".js-contact-name").text(contact);
            $card.find(".js-contact-title").text(title);
            if (!contact || !title) {
                $card.find(".js-contact-sep").hide();
            }
            if (!contact && !title) {
                $card.find(".vas-invovw-detailMeta").first().hide();
            }

            $card.find(".js-due-label").text(VIS.Msg.getMsg("DueDate"));
            $card.find(".js-due-date").text(formatDate(data.DueDate));

            var $duePill = $card.find(".js-due-pill");
            if (data.DueDate) {
                var diff = data.DaysDifference || 0;
                if (data.IsOverdue) {
                    $duePill.text(diff + " " + VIS.Msg.getMsg("VAS_DaysOverdue"));
                    $duePill.addClass("tone-overdue");
                } else {
                    $duePill.text(diff + " " + VIS.Msg.getMsg("VAS_DaysRemaining"));
                    $duePill.addClass("tone-remaining");
                }
            } else {
                $duePill.hide();
            }

            $card.find(".js-out-label").text(VIS.Msg.getMsg("VAS_Outstanding"));
            $card.find(".js-out-amt").text(formatAmount(
                +(data.Outstanding || 0),
                data.CurSymbol, data.ISO_Code, data.StdPrecision));

            var grand = +data.GrandTotal || 0;
            var outAmt = +data.Outstanding || 0;
            var $outMeta = $card.find(".js-out-meta");
            if (grand > 0) {
                var pct = Math.round((outAmt / grand) * 100);
                $outMeta.text(pct + "% " + VIS.Msg.getMsg("VAS_OfTotal"));
            } else {
                $outMeta.hide();
            }

            renderTimeline($card.find(".js-timeline"));
            renderEligibility($card.find(".js-eligible-section"));
            renderLines($card.find(".js-lines-section"));
            renderPaymentRisk($card.find(".js-risk-section"));
            renderNotes($card.find(".js-notes-section"));

            $body.append($card);
        }

        function renderPaymentRisk($section) {
            // Reset prior state.
            $section.removeClass("tone-low tone-medium tone-high tone-none");

            var level = (data && data.PaymentRiskLevel) || "none";
            var bp    = (data && data.BPName) || VIS.Msg.getMsg("Customer");

            var titleText, subText, badgeText, subKey;
            if (level === "none") {
                titleText = VIS.Msg.getMsg("VAS_NoPaymentHistoryTitle").replace("{0}", bp);
                subText   = VIS.Msg.getMsg("VAS_NoPreviousPaidInvoicesFound");
                badgeText = VIS.Msg.getMsg("VAS_NoHistory");
            } else {
                var days = +data.AvgDaysToPay || 0;
                titleText = VIS.Msg.getMsg("VAS_CustomerPaysInDaysOnAverage")
                    .replace("{0}", bp)
                    .replace("{1}", days);

                if (level === "low") {
                    subKey    = "VAS_LikelyToClearBeforeDueDate";
                    badgeText = VIS.Msg.getMsg("VAS_LowRisk");
                } else if (level === "medium") {
                    subKey    = "VAS_UsuallyPaysShortlyAfterDueDate";
                    badgeText = VIS.Msg.getMsg("VAS_MediumRisk");
                } else {
                    subKey    = "VAS_FrequentlyPaysLate";
                    badgeText = VIS.Msg.getMsg("VAS_HighRisk");
                }
                subText = VIS.Msg.getMsg(subKey);
            }

            $section.addClass("tone-" + level);
            $section.find(".js-risk-title").text(titleText);
            $section.find(".js-risk-sub").text(subText);
            $section.find(".js-risk-badge").text(badgeText);
        }

        function renderNotes($section) {
            var notes = (data && data.Notes) || [];
            var visiblePdf = (data && data.VisibleOnPDFCount) || 0;

            $section.find(".js-notes-title").text(VIS.Msg.getMsg("Notes"));
            $section.find(".js-notes-add").text(VIS.Msg.getMsg("VAS_AddNote"));

            // Header meta line: "<n> notes · <m> visible on invoice PDF"
            var $meta = $section.find(".js-notes-meta").empty();
            var notesLabel = (notes.length === 1)
                ? VIS.Msg.getMsg("VAS_NoteCountOne")
                : VIS.Msg.getMsg("VAS_NoteCountMany");
            $meta.append($('<span></span>').text(notes.length + " " + notesLabel));
            if (notes.length > 0) {
                $meta.append($('<span class="vas-invovw-notesDot"></span>').text("·"));
                $meta.append($('<span></span>').text(
                    visiblePdf + " " + VIS.Msg.getMsg("VAS_VisibleOnInvoicePDF")));
            }

            // Add-note click handler — open the platform chat popup.
            $section.find(".js-notes-add")
                .off("click.vasInvOvwNote")
                .on("click.vasInvOvwNote", function () {
                    openChatPopup();
                });

            var $list = $section.find(".js-notes-list").empty();
            var $empty = $section.find(".js-notes-empty");

            if (!notes.length) {
                $list.hide();
                $empty.text(VIS.Msg.getMsg("VAS_NoNotes")).show();
                return;
            }
            $empty.hide();
            $list.show();

            for (var i = 0; i < notes.length; i++) {
                $list.append(buildNoteRow(notes[i]));
            }
        }

        function openChatPopup() {
            if (!data || !data.C_Invoice_ID) return;
            if (!VIS || !VIS.Chat) {
                console.log("VIS.Chat not available");
                return;
            }

            var recordID = $self.record_ID || data.C_Invoice_ID;
            var tableID  = $self.table_ID;
            if (!tableID && $self.curTab && typeof $self.curTab.getAD_Table_ID === "function") {
                tableID = $self.curTab.getAD_Table_ID();
            }

            var chatID = 0;
            if ($self.curTab && typeof $self.curTab.getCM_ChatID === "function") {
                chatID = $self.curTab.getCM_ChatID() || 0;
            }

            var infoLabel = VIS.Msg.getMsg("VAS_InvoiceNumber");
            var description = (infoLabel ? infoLabel + ": " : "") + (data.DocumentNo || "");

            var chat = new VIS.Chat(recordID, chatID, tableID, description, $self.windowNo);
            chat.onClose = function () {
                if ($self.curTab && typeof $self.curTab.loadChats === "function") {
                    try { $self.curTab.loadChats(); } catch (e) { /* ignore */ }
                }
                // Refresh the panel so new chat entries show up.
                $self.fetchData(recordID);
            };
            chat.show();
        }

        function buildNoteRow(n) {
            var $row = $(
                '<div class="vas-invovw-noteRow">' +
                    '<div class="vas-invovw-avatar"></div>' +
                    '<div class="vas-invovw-noteBody">' +
                        '<div class="vas-invovw-noteHead">' +
                            '<span class="vas-invovw-noteAuthor"></span>' +
                            '<span class="vas-invovw-noteDate"></span>' +
                            '<span class="vas-invovw-notePill"></span>' +
                        '</div>' +
                        '<div class="vas-invovw-noteText"></div>' +
                    '</div>' +
                '</div>'
            );

            var name = n.UserName || "";
            var $av = $row.find(".vas-invovw-avatar");
            $av.text(initialsFromName(name));
            $av.addClass(avatarTone(n.AD_User_ID));

            var headTitle = n.Subject ? n.Subject : (name || "");
            $row.find(".vas-invovw-noteAuthor").text(headTitle);

            var dateLabel = "";
            if (n.IsEdited && n.Updated) {
                dateLabel = VIS.Msg.getMsg("VAS_Edited") + " " + formatDateTime(n.Updated);
            } else if (n.Created) {
                dateLabel = formatDateTime(n.Created);
            }
            $row.find(".vas-invovw-noteDate").text(dateLabel);

            var $pill = $row.find(".vas-invovw-notePill");
            if (n.IsVisibleToCustomer) {
                $pill.text(VIS.Msg.getMsg("VAS_VisibleToCustomer"))
                     .addClass("tone-public");
            } else {
                $pill.text(VIS.Msg.getMsg("VAS_Internal"))
                     .addClass("tone-internal");
            }

            $row.find(".vas-invovw-noteText").text(n.CharacterData || "");
            return $row;
        }

        function initialsFromName(name) {
            if (!name) return "?";
            var parts = name.trim().split(/\s+/);
            var first = parts[0] ? parts[0].charAt(0) : "";
            var last  = parts.length > 1 ? parts[parts.length - 1].charAt(0) : "";
            return (first + last).toUpperCase() || first.toUpperCase();
        }

        function avatarTone(userId) {
            var tones = ["a", "b", "c", "d", "e", "f"];
            var n = parseInt(userId, 10);
            if (isNaN(n) || n < 0) n = 0;
            return "tone-" + tones[n % tones.length];
        }

        function formatDateTime(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                var datePart = d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
                var timePart = d.toLocaleTimeString(window.navigator.language, {
                    hour: "2-digit", minute: "2-digit"
                });
                return datePart + " · " + timePart;
            } catch (e) {
                return d.toString();
            }
        }

        function renderEligibility($section) {
            var eligible = +(data && data.EligibleRecurringCount) || 0;
            var physical = +(data && data.PhysicalItemsCount)    || 0;
            var totalRec = +(data && data.RecurringLineCount)    || 0;

            // Section is always shown; only the button visibility depends on count.
            $section.show();
            $section.toggleClass("is-empty", eligible <= 0);

            $section.find(".js-eligible-title").text(VIS.Msg.getMsg("VAS_EligibleToMakeRecurring"));

            var $sub = $section.find(".js-eligible-sub").empty();
            if (eligible <= 0) {
                $sub.append($('<span></span>').text(
                    VIS.Msg.getMsg("VAS_NoLinesEligibleForRecurring")));
                if (physical > 0) {
                    $sub.append($('<span class="vas-invovw-eligibleDot"></span>').text("·"));
                    $sub.append($('<span></span>').text(
                        VIS.Msg.getMsg("VAS_PhysicalItemsDetected")
                            .replace("{0}", physical)));
                }
            } else if (physical === 0 && eligible === totalRec) {
                $sub.append($('<span></span>').text(
                    VIS.Msg.getMsg("VAS_AllLinesAreServicesOrExpenses")
                        .replace("{0}", totalRec)));
                $sub.append($('<span class="vas-invovw-eligibleDot"></span>').text("·"));
                $sub.append($('<span></span>').text(
                    VIS.Msg.getMsg("VAS_NoPhysicalItemsDetected")));
            } else {
                $sub.append($('<span></span>').text(
                    VIS.Msg.getMsg("VAS_LinesEligibleForRecurring")
                        .replace("{0}", eligible)
                        .replace("{1}", totalRec)));
                if (physical > 0) {
                    $sub.append($('<span class="vas-invovw-eligibleDot"></span>').text("·"));
                    $sub.append($('<span></span>').text(
                        VIS.Msg.getMsg("VAS_PhysicalItemsDetected")
                            .replace("{0}", physical)));
                }
            }

            var $btn = $section.find(".js-eligible-btn");
            if (eligible > 0) {
                $btn.show();
                $btn.text(VIS.Msg.getMsg("VAS_SetUpRecurring"));
                $btn.off("click.vasInvOvwRec").on("click.vasInvOvwRec", function () {
                    var info = (VIS && VIS.ADialog && VIS.ADialog.info)
                        ? VIS.ADialog.info
                        : function (msg) { console.log(msg); };
                    info(VIS.Msg.getMsg("VAS_SetUpRecurring"));
                });
            } else {
                $btn.hide();
                $btn.off("click.vasInvOvwRec");
            }
        }

        function renderLines($section) {
            var lines = (data && data.Lines) || [];
            $section.find(".js-lines-title").text(VIS.Msg.getMsg("VAS_LineItems"));

            if (!lines.length) {
                $section.find(".js-lines-thead").empty();
                $section.find(".js-lines-body").empty();
                $section.find(".js-lines-totals").empty();
                $section.find(".vas-invovw-linesTableWrap").hide();
                $section.find(".js-lines-totals").hide();
                return;
            }
            $section.find(".vas-invovw-linesTableWrap").show();
            $section.find(".js-lines-totals").show();

            var $thead = $section.find(".js-lines-thead").empty();
            $thead
                .append($('<th class="vas-invovw-thDesc"></th>')
                    .text(VIS.Msg.getMsg("Description")))
                .append($('<th class="vas-invovw-thType"></th>')
                    .text(VIS.Msg.getMsg("Type")))
                .append($('<th class="vas-invovw-thQty"></th>')
                    .text(VIS.Msg.getMsg("Quantity")))
                .append($('<th class="vas-invovw-thRate"></th>')
                    .text(VIS.Msg.getMsg("VAS_Rate")))
                .append($('<th class="vas-invovw-thAmt"></th>')
                    .text(VIS.Msg.getMsg("Amount")));

            var $tbody = $section.find(".js-lines-body").empty();
            for (var i = 0; i < lines.length; i++) {
                var ln = lines[i];
                var typeText = ln.TypeLabel || "";
                var typeCode = (ln.TypeCode || "").toUpperCase();

                var qtyTxt  = formatNumber(+ln.QtyInvoiced || 0, +ln.UOMPrecision || 0);
                if (ln.UOMSymbol) qtyTxt += " " + ln.UOMSymbol;

                var rateTxt = formatAmount(
                    +ln.PriceActual || 0, data.CurSymbol, data.ISO_Code,
                    ln.PricePrecision != null ? ln.PricePrecision : data.StdPrecision);
                var amtTxt  = formatAmount(
                    +ln.LineNetAmt || 0, data.CurSymbol, data.ISO_Code, data.StdPrecision);

                var $tr = $('<tr class="vas-invovw-trLine"></tr>');

                var $tdDesc = $('<td class="vas-invovw-tdDesc"></td>');
                $tdDesc.append($('<div class="vas-invovw-lineName"></div>')
                    .text(ln.ProductName || ln.ChargeName || ""));
                if (ln.Description) {
                    $tdDesc.append($('<div class="vas-invovw-lineDesc"></div>')
                        .text(ln.Description));
                }
                $tr.append($tdDesc);

                var $tdType = $('<td class="vas-invovw-tdType"></td>');
                if (typeText) {
                    $tdType.append($('<span class="vas-invovw-typePill"></span>')
                        .text(typeText)
                        .addClass(toneClassForTypeCode(typeCode)));
                }
                $tr.append($tdType);

                $tr.append($('<td class="vas-invovw-tdQty"></td>').text(qtyTxt));
                $tr.append($('<td class="vas-invovw-tdRate"></td>').text(rateTxt));
                $tr.append($('<td class="vas-invovw-tdAmt"></td>').text(amtTxt));

                if (ln.IsDescription) $tr.addClass("is-description");
                $tbody.append($tr);
            }

            renderLineTotals($section.find(".js-lines-totals"));
        }

        function renderLineTotals($container) {
            $container.empty();
            var precision = (data && data.StdPrecision >= 0) ? data.StdPrecision : 2;
            var rows = [
                { msgKey: "VAS_Subtotal", value: +data.TotalLines || 0, cssClass: "" },
                { msgKey: "VAS_Tax",      value: +data.TaxAmt     || 0, cssClass: "" },
                { msgKey: "Total",        value: +data.GrandTotal || 0, cssClass: "is-grand" }
            ];
            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var $row = $('<div class="vas-invovw-totalRow"></div>').addClass(row.cssClass);
                $row.append($('<span class="vas-invovw-totalLabel"></span>')
                    .text(VIS.Msg.getMsg(row.msgKey)));
                $row.append($('<span class="vas-invovw-totalValue"></span>')
                    .text(formatAmount(row.value, data.CurSymbol, data.ISO_Code, precision)));
                $container.append($row);
            }
        }

        function toneClassForTypeCode(code) {
            switch (code) {
                case "I": return "tone-item";
                case "S": return "tone-service";
                case "E": return "tone-expense";
                case "R": return "tone-resource";
                case "O": return "tone-online";
            }
            return "tone-default";
        }

        function formatNumber(value, precision) {
            var p = (precision >= 0) ? precision : 0;
            return value.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
        }

        function renderActions($row) {
            var labels = {
                "record-payment": "VAS_RecordPayment",
                "send-reminder":  "VAS_SendReminder",
                "download-pdf":   "VAS_DownloadPDF",
                "duplicate":      "VAS_Duplicate",
                "view-ledger":    "VAS_ViewLedgerEntry"
            };
            $row.find(".vas-invovw-btn").each(function () {
                var $b = $(this);
                var key = $b.attr("data-action");
                $b.text(VIS.Msg.getMsg(labels[key]));
            });

            // Disable when no invoice context.
            var hasRecord = !!(data && data.C_Invoice_ID);
            $row.find(".vas-invovw-btn").prop("disabled", !hasRecord);

            // Duplicate is also disabled when the source invoice is voided/reversed.
            var docStatus = (data && data.DocStatus) || "";
            var canDuplicate = hasRecord && docStatus !== "VO" && docStatus !== "RE";
            $row.find('.vas-invovw-btn[data-action="duplicate"]').prop("disabled", !canDuplicate);

            // Record payment requires DocStatus IN ('CO','CL') and Outstanding > 0.
            var outstanding = +(data && data.Outstanding) || 0;
            var canPay = hasRecord
                && (docStatus === "CO" || docStatus === "CL")
                && outstanding > 0;
            $row.find('.vas-invovw-btn[data-action="record-payment"]').prop("disabled", !canPay);

            $row.off("click.vasInvOvw").on("click.vasInvOvw", ".vas-invovw-btn", function (e) {
                e.preventDefault();
                if ($(this).is(":disabled")) return;
                handleAction($(this).attr("data-action"));
            });
        }

        function handleAction(action) {
            if (!data || !data.C_Invoice_ID) return;
            var info = (VIS && VIS.ADialog && VIS.ADialog.info)
                       ? VIS.ADialog.info
                       : function (msg) { console.log(msg); };
            switch (action) {
                case "record-payment":
                    openRecordPaymentDialog();
                    break;
                case "send-reminder":
                    info(VIS.Msg.getMsg("VAS_SendReminder"));
                    break;
                case "download-pdf":
                    info(VIS.Msg.getMsg("VAS_DownloadPDF"));
                    break;
                case "duplicate":
                    duplicateInvoice();
                    break;
                case "view-ledger":
                    info(VIS.Msg.getMsg("VAS_ViewLedgerEntry"));
                    break;
            }
        }

        function openRecordPaymentDialog() {
            if (!data || !data.C_Invoice_ID) return;

            var docStatus = data.DocStatus || "";
            var outstanding = +(data.Outstanding) || 0;
            if ((docStatus !== "CO" && docStatus !== "CL") || outstanding <= 0) {
                var warn = (VIS && VIS.ADialog && VIS.ADialog.warn)
                    ? VIS.ADialog.warn
                    : function (msg) { console.log(msg); };
                warn(VIS.Msg.getMsg("VAS_RecordPaymentNotAllowed"));
                return;
            }

            $.ajax({
                url:  VIS.Application.contextUrl + "VAS_InvoiceOverview/GetRecordPaymentMeta",
                type: "GET",
                dataType: "json",
                data: { C_Invoice_ID: $self.record_ID },
                success: function (raw) {
                    var meta = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    console.log("RecordPayment meta:", meta);
                    if (!meta || !meta.C_BPartner_ID) {
                        console.log("RecordPayment meta empty", meta);
                        return;
                    }
                    showRecordPaymentDialog(meta);
                },
                error: function (err) { console.log(err); }
            });
        }

        function showRecordPaymentDialog(meta) {
            var precision = meta.StdPrecision >= 0 ? meta.StdPrecision : 2;
            var outstanding = +meta.Outstanding || 0;
            var docNo = data.DocumentNo || "";

            var $form = $('<div class="vas-invovw-rpDialog"></div>');

            // ---- Subline (invoice no + outstanding) -------------------------
            var $sub = $('<div class="vas-invovw-rpSubline"></div>');
            $sub.append($('<span></span>').text(docNo));
            $sub.append($('<span class="vas-invovw-rpDot"></span>').text("·"));
            $sub.append($('<span></span>').text(
                VIS.Msg.getMsg("VAS_Outstanding") + " " +
                formatAmount(outstanding, meta.CurSymbol, meta.ISO_Code, precision)));
            $form.append($sub);

            // ---- Amount Received --------------------------------------------
            var $amtBlock = $(
                '<div class="vas-invovw-rpField vas-invovw-rpAmtBlock">' +
                    '<div class="vas-invovw-rpLabel"></div>' +
                    '<div class="vas-invovw-rpAmtToggle">' +
                        '<label class="vas-invovw-rpRadio is-active">' +
                            '<input type="radio" name="rp-amt-mode" value="full" checked>' +
                            '<span></span>' +
                        '</label>' +
                        '<label class="vas-invovw-rpRadio">' +
                            '<input type="radio" name="rp-amt-mode" value="partial">' +
                            '<span></span>' +
                        '</label>' +
                    '</div>' +
                    '<div class="vas-invovw-rpAmtInputWrap">' +
                        '<div class="vas-invovw-rpAmtFieldWrap">' +
                            '<span class="vas-invovw-rpAmtCur"></span>' +
                            '<input type="text" class="vas-invovw-rpAmt" />' +
                        '</div>' +
                        '<select class="vas-invovw-rpCurSel"></select>' +
                    '</div>' +
                    '<div class="vas-invovw-rpAmtError js-rp-amt-error" style="display:none;"></div>' +
                '</div>'
            );
            $amtBlock.find(".vas-invovw-rpLabel").text(VIS.Msg.getMsg("VAS_AmountReceived"));
            $amtBlock.find(".vas-invovw-rpRadio").eq(0).find("span").text(VIS.Msg.getMsg("VAS_Full"));
            $amtBlock.find(".vas-invovw-rpRadio").eq(1).find("span").text(VIS.Msg.getMsg("VAS_Partial"));

            var $amt    = $amtBlock.find(".vas-invovw-rpAmt");
            var $amtCur = $amtBlock.find(".vas-invovw-rpAmtCur");
            var $curSel = $amtBlock.find(".vas-invovw-rpCurSel");

            $amtCur.text(meta.CurSymbol || "");
            $amt.val(outstanding.toFixed(precision));

            // Currency dropdown — populated from meta.Currencies (IsMyCurrency=Y).
            var currencyMap = {};
            (meta.Currencies || []).forEach(function (c) {
                currencyMap[c.C_Currency_ID] = c;
                $curSel.append($('<option></option>')
                    .val(c.C_Currency_ID)
                    .text(c.ISO_Code || ""));
            });
            // If the invoice currency isn't in the "my currency" list, add it
            // so the dialog still reflects the underlying invoice value.
            if (meta.C_Currency_ID && !currencyMap[meta.C_Currency_ID]) {
                currencyMap[meta.C_Currency_ID] = {
                    C_Currency_ID: meta.C_Currency_ID,
                    ISO_Code:      meta.ISO_Code,
                    CurSymbol:     meta.CurSymbol,
                    StdPrecision:  meta.StdPrecision
                };
                $curSel.append($('<option></option>')
                    .val(meta.C_Currency_ID)
                    .text(meta.ISO_Code || ""));
            }
            $curSel.val(meta.C_Currency_ID);

            // Update the leading symbol + precision whenever the currency changes.
            $curSel.on("change", function () {
                var sel = currencyMap[parseInt($curSel.val(), 10)];
                if (!sel) return;
                $amtCur.text(sel.CurSymbol || sel.ISO_Code || "");
                if (sel.StdPrecision >= 0) precision = sel.StdPrecision;
                var amt = parseFloat($amt.val()) || 0;
                $amt.val(amt.toFixed(precision));
                updateRecordButton();
            });

            // Full = read-only amount + currency; Partial = editable.
            function applyMode(mode) {
                var partial = (mode === "partial");
                $amt.prop("disabled", !partial);
                $amt.toggleClass("is-readonly", !partial);
                $curSel.prop("disabled", !partial);
                $amtBlock.find(".vas-invovw-rpRadio").removeClass("is-active");
                $amtBlock.find('input[name="rp-amt-mode"][value="' + mode + '"]')
                    .closest(".vas-invovw-rpRadio").addClass("is-active");
                if (!partial) $amt.val(outstanding.toFixed(precision));
                updateRecordButton();
            }
            $amtBlock.find('input[name="rp-amt-mode"]').on("change", function () {
                applyMode($(this).val());
            });
            applyMode("full");
            $form.append($amtBlock);

            // ---- Form fields (two-column rows) ------------------------------
            var $row1 = $(
                '<div class="vas-invovw-rpRow">' +
                    '<div class="vas-invovw-rpField">' +
                        '<div class="vas-invovw-rpLabel"></div>' +
                        '<input type="date" class="vas-invovw-rpDate" />' +
                    '</div>' +
                    '<div class="vas-invovw-rpField">' +
                        '<div class="vas-invovw-rpLabel"></div>' +
                        '<select class="vas-invovw-rpMethod"></select>' +
                    '</div>' +
                '</div>'
            );
            $row1.find(".vas-invovw-rpField").eq(0).find(".vas-invovw-rpLabel")
                .text(VIS.Msg.getMsg("VAS_PaymentDate"));
            $row1.find(".vas-invovw-rpField").eq(1).find(".vas-invovw-rpLabel")
                .text(VIS.Msg.getMsg("VAS_PaymentMethod"));

            var $date    = $row1.find(".vas-invovw-rpDate");
            var $method  = $row1.find(".vas-invovw-rpMethod");
            var today = new Date();
            $date.val(today.toISOString().slice(0, 10));

            $method.append($('<option></option>').val("").text(""));
            (meta.PaymentMethods || []).forEach(function (m) {
                var $o = $('<option></option>')
                    .val(m.VA009_PaymentMethod_ID)
                    .text(m.Name)
                    .attr("data-base-type", m.BaseType || "");
                $method.append($o);
            });
            $form.append($row1);

            var $row2 = $(
                '<div class="vas-invovw-rpRow">' +
                    '<div class="vas-invovw-rpField">' +
                        '<div class="vas-invovw-rpLabel"></div>' +
                        '<input type="text" class="vas-invovw-rpRef" maxlength="120" />' +
                    '</div>' +
                    '<div class="vas-invovw-rpField">' +
                        '<div class="vas-invovw-rpLabel"></div>' +
                        '<select class="vas-invovw-rpBank"></select>' +
                    '</div>' +
                '</div>'
            );
            $row2.find(".vas-invovw-rpField").eq(0).find(".vas-invovw-rpLabel")
                .text(VIS.Msg.getMsg("VAS_ReferenceTrxId"));
            $row2.find(".vas-invovw-rpField").eq(1).find(".vas-invovw-rpLabel")
                .text(VIS.Msg.getMsg("VAS_DepositTo"));

            var $ref  = $row2.find(".vas-invovw-rpRef");
            var $bank = $row2.find(".vas-invovw-rpBank");
            $bank.append($('<option></option>').val("").text(""));
            (meta.BankAccounts || []).forEach(function (b) {
                var label = (b.BankName || "") +
                            (b.AccountNo ? "  ··" + b.AccountNo.slice(-4) : "");
                $bank.append($('<option></option>')
                    .val(b.C_BankAccount_ID).text(label));
            });
            if (meta.DefaultC_BankAccount_ID) $bank.val(meta.DefaultC_BankAccount_ID);
            $form.append($row2);

            // Currency Type row: Currency Type on the left, Check No (conditional)
            // sits in the right slot so it appears immediately after Currency Type.
            var $row3 = $(
                '<div class="vas-invovw-rpRow">' +
                    '<div class="vas-invovw-rpField">' +
                        '<div class="vas-invovw-rpLabel"></div>' +
                        '<select class="vas-invovw-rpConv"></select>' +
                    '</div>' +
                    '<div class="vas-invovw-rpField js-check-no-cell" style="display:none;">' +
                        '<div class="vas-invovw-rpLabel"></div>' +
                        '<input type="text" class="vas-invovw-rpCheckNo" maxlength="40" />' +
                    '</div>' +
                '</div>'
            );
            $row3.find(".vas-invovw-rpField").eq(0).find(".vas-invovw-rpLabel")
                .text(VIS.Msg.getMsg("VAS_CurrencyType"));
            $row3.find(".js-check-no-cell").find(".vas-invovw-rpLabel")
                .text(VIS.Msg.getMsg("CheckNo"));

            var $conv      = $row3.find(".vas-invovw-rpConv");
            var $checkNo   = $row3.find(".vas-invovw-rpCheckNo");
            var $checkNoCell = $row3.find(".js-check-no-cell");

            $conv.append($('<option></option>').val("").text(""));
            (meta.ConversionTypes || []).forEach(function (c) {
                $conv.append($('<option></option>').val(c.C_ConversionType_ID).text(c.Name));
            });
            if (meta.DefaultC_ConversionType_ID) $conv.val(meta.DefaultC_ConversionType_ID);
            $form.append($row3);

            // ---- Conditional Check Date row (alone) ------------------------
            var $checkDateRow = $(
                '<div class="vas-invovw-rpRow js-check-date-row" style="display:none;">' +
                    '<div class="vas-invovw-rpField">' +
                        '<div class="vas-invovw-rpLabel"></div>' +
                        '<input type="date" class="vas-invovw-rpCheckDate" />' +
                    '</div>' +
                    '<div class="vas-invovw-rpField"></div>' +
                '</div>'
            );
            $checkDateRow.find(".vas-invovw-rpField").eq(0).find(".vas-invovw-rpLabel")
                .text(VIS.Msg.getMsg("VAS_CheckDate"));
            var $checkDate = $checkDateRow.find(".vas-invovw-rpCheckDate");
            $form.append($checkDateRow);

            // VA009_PaymentBaseType code 'S' = Check.
            $method.on("change", function () {
                var bt = $method.find("option:selected").attr("data-base-type") || "";
                var isCheck = (bt === "S");
                $checkNoCell.toggle(isCheck);
                $checkDateRow.toggle(isCheck);
                if (!isCheck) {
                    $checkNo.val("");
                    $checkDate.val("");
                }
            });

            // ---- Footer with Cancel / Record buttons -----------------------
            var $footer = $(
                '<div class="vas-invovw-rpFooter">' +
                    '<button type="button" class="vas-invovw-btn is-outline js-rp-cancel"></button>' +
                    '<button type="button" class="vas-invovw-btn is-primary js-rp-record"></button>' +
                '</div>'
            );
            $footer.find(".js-rp-cancel").text(VIS.Msg.getMsg("Cancel"));
            var $btnRecord = $footer.find(".js-rp-record");
            $form.append($footer);

            function currentCurrency() {
                return currencyMap[parseInt($curSel.val(), 10)] || {
                    CurSymbol: meta.CurSymbol,
                    ISO_Code:  meta.ISO_Code
                };
            }

            function updateRecordButton() {
                if (!$btnRecord || !$btnRecord.length) return;
                var amt = parseFloat($amt.val()) || 0;
                var cc  = currentCurrency();

                // Pay Amount must not exceed Outstanding.
                var $err = $amtBlock.find(".js-rp-amt-error");
                var exceeds = (amt > outstanding);
                if (exceeds) {
                    $err.text(VIS.Msg.getMsg("VAS_PaymentExceedsOutstanding"));
                    $err.show();
                    $amt.addClass("is-error");
                } else {
                    $err.hide().text("");
                    $amt.removeClass("is-error");
                }

                $btnRecord.text(VIS.Msg.getMsg("VAS_RecordPayment") + " " +
                                formatAmount(amt, cc.CurSymbol, cc.ISO_Code, precision));
                $btnRecord.prop("disabled", !(amt > 0) || exceeds);
            }
            $amt.on("input", updateRecordButton);
            updateRecordButton();

            // ---- Open dialog ------------------------------------------------
            var dlg = new VIS.ChildDialog();
            dlg.setContent($form);
            dlg.setHeight(560);
            dlg.setWidth(640);
            dlg.setTitle(VIS.Msg.getMsg("VAS_RecordPayment"));
            dlg.setModal(true);

            $footer.find(".js-rp-cancel").on("click", function () {
                dlg.close ? dlg.close() : dlg.dispose && dlg.dispose();
            });

            $btnRecord.on("click", function () {
                if ($btnRecord.is(":disabled")) return;
                submitRecordPayment(meta, dlg, {
                    isFull:   $amtBlock.find('input[name="rp-amt-mode"]:checked').val() === "full",
                    payAmt:   parseFloat($amt.val()) || 0,
                    currency: parseInt($curSel.val(), 10) || meta.C_Currency_ID,
                    method:   parseInt($method.val(), 10) || 0,
                    bank:     parseInt($bank.val(), 10) || 0,
                    convType: parseInt($conv.val(), 10) || meta.DefaultC_ConversionType_ID,
                    date:     $date.val(),
                    refNo:    $ref.val(),
                    checkNo:  $checkNo.val(),
                    checkDate: $checkDate.val()
                });
            });

            dlg.show();
            if (typeof dlg.hidebuttons === "function") {
                try { dlg.hidebuttons(); } catch (e) { /* ignore */ }
            }
        }

        function submitRecordPayment(meta, dlg, vals) {
            var info = (VIS && VIS.ADialog && VIS.ADialog.info)
                ? VIS.ADialog.info
                : function (msg) { console.log(msg); };
            var err  = (VIS && VIS.ADialog && VIS.ADialog.error)
                ? VIS.ADialog.error
                : function (msg) { console.log(msg); };

            var payload = {
                C_Invoice_ID:           $self.record_ID,
                IsFull:                 vals.isFull,
                PayAmt:                 vals.payAmt,
                C_Currency_ID:          vals.currency,
                C_ConversionType_ID:    vals.convType,
                C_BankAccount_ID:       vals.bank,
                VA009_PaymentMethod_ID: vals.method,
                DateTrx:                vals.date,
                ReferenceNo:            vals.refNo,
                CheckNo:                vals.checkNo,
                CheckDate:              vals.checkDate
            };

            $.ajax({
                url:      VIS.Application.contextUrl + "VAS_InvoiceOverview/RecordPayment",
                type:     "POST",
                dataType: "json",
                data:     { payload: JSON.stringify(payload) },
                success:  function (raw) {
                    var resp = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (resp && resp.Success) {
                        if (dlg && dlg.close) dlg.close();
                        else if (dlg && dlg.dispose) dlg.dispose();
                        info(VIS.Msg.getMsg("VAS_RecordPaymentSuccess")
                            .replace("{0}", resp.DocumentNo || "")
                            .replace("{1}", formatAmount(
                                +resp.PayAmt || 0,
                                meta.CurSymbol, meta.ISO_Code,
                                meta.StdPrecision)));
                        $self.fetchData($self.record_ID);
                    } else {
                        err((resp && resp.Message)
                            ? resp.Message
                            : VIS.Msg.getMsg("VAS_RecordPaymentFailed"));
                    }
                },
                error: function (xhr) {
                    console.log(xhr);
                    err(VIS.Msg.getMsg("VAS_RecordPaymentFailed"));
                }
            });
        }

        function duplicateInvoice() {
            if (!data || !data.C_Invoice_ID) return;

            var docStatus = data.DocStatus || "";
            if (docStatus === "VO" || docStatus === "RE") {
                var warn = (VIS && VIS.ADialog && VIS.ADialog.warn)
                    ? VIS.ADialog.warn
                    : function (msg) { console.log(msg); };
                warn(VIS.Msg.getMsg("VAS_DuplicateNotAllowedVoidedReversed"));
                return;
            }

            var info = (VIS && VIS.ADialog && VIS.ADialog.info)
                ? VIS.ADialog.info
                : function (msg) { console.log(msg); };
            var err  = (VIS && VIS.ADialog && VIS.ADialog.error)
                ? VIS.ADialog.error
                : function (msg) { console.log(msg); };

            showBusy(true);
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_InvoiceOverview/DuplicateInvoice",
                type: "POST",
                dataType: "json",
                data: { C_Invoice_ID: $self.record_ID },
                success: function (raw) {
                    showBusy(false);
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    if (parsed && parsed.Success) {
                        var msg = VIS.Msg.getMsg("VAS_DuplicateSuccess")
                                     .replace("{0}", parsed.NewDocumentNo || "");
                        info(msg);
                    } else {
                        err((parsed && parsed.Message)
                            ? parsed.Message
                            : VIS.Msg.getMsg("VAS_DuplicateFailed"));
                    }
                },
                error: function (xhr) {
                    showBusy(false);
                    console.log(xhr);
                    err(VIS.Msg.getMsg("VAS_DuplicateFailed"));
                }
            });
        }

        function renderTimeline($container) {
            $container.empty();

            var steps = [
                {
                    msgKey: "VAS_Draft",
                    done:   true,
                    name:   data.CreatedByName || "",
                    date:   formatDateShort(data.Created),
                    pendingText: ""
                },
                {
                    msgKey: "VAS_Approved",
                    done:   !!data.IsApproved,
                    name:   data.IsApproved ? (data.ApprovedByName || "") : "",
                    date:   data.IsApproved ? formatDateShort(data.ApprovedDate) : "",
                    pendingText: ""
                },
                {
                    msgKey: "VAS_Sent",
                    done:   !!data.IsSent,
                    name:   "",
                    date:   data.IsSent ? formatDateShort(data.DateInvoiced) : "",
                    pendingText: "",
                    activeUsesSince: true
                },
                {
                    msgKey: "VAS_Paid",
                    done:   !!data.IsFullyPaid,
                    name:   "",
                    date:   data.IsFullyPaid ? formatDateShort(data.PaidDate) : "",
                    pendingText: data.DueDate
                                 ? VIS.Msg.getMsg("VAS_Due") + " " + formatDateShort(data.DueDate)
                                 : ""
                },
                {
                    msgKey: "VAS_Closed",
                    done:   !!data.IsClosed,
                    name:   "",
                    date:   "",
                    pendingText: VIS.Msg.getMsg("Pending")
                }
            ];

            // Active step = the last "done" step in the sequence.
            var activeIdx = -1;
            for (var k = 0; k < steps.length; k++) {
                if (steps[k].done) activeIdx = k;
            }

            for (var i = 0; i < steps.length; i++) {
                var s = steps[i];
                var isActive = (i === activeIdx);

                var $step = $(
                    '<div class="vas-invovw-tlStep">' +
                        '<div class="vas-invovw-tlLabel"></div>' +
                        '<div class="vas-invovw-tlSub"></div>' +
                    '</div>'
                );
                $step.toggleClass("is-done",    s.done);
                $step.toggleClass("is-pending", !s.done);
                $step.toggleClass("is-active",  isActive);
                $step.find(".vas-invovw-tlLabel").text(VIS.Msg.getMsg(s.msgKey));

                var $sub = $step.find(".vas-invovw-tlSub");
                if (s.done && isActive && s.activeUsesSince) {
                    var bits = [];
                    if (s.date) bits.push(VIS.Msg.getMsg("VAS_Since") + " " + s.date);
                    bits.push(VIS.Msg.getMsg("Active"));
                    $sub.text(bits.join(" · "));
                } else if (s.done) {
                    var parts = [];
                    if (s.name) parts.push(s.name);
                    if (s.date) parts.push(s.date);
                    $sub.text(parts.join(" · ") || " ");
                    if (parts.length) {
                        $sub.append(' ').append(
                            $('<i class="fa fa-check vas-invovw-tlCheck" aria-hidden="true"></i>'));
                    }
                } else {
                    $sub.text(s.pendingText || " ");
                }

                $container.append($step);
            }
        }

        function formatDateShort(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    month: "short", day: "2-digit"
                });
            } catch (e) {
                return d.toDateString();
            }
        }

        function formatDate(value) {
            if (!value) return "";
            var d = (value instanceof Date) ? value : new Date(value);
            if (isNaN(d.getTime())) return "";
            try {
                return d.toLocaleDateString(window.navigator.language, {
                    year: "numeric", month: "short", day: "2-digit"
                });
            } catch (e) {
                return d.toDateString();
            }
        }

        function formatAmount(value, symbol, iso, precision) {
            var sign = value < 0 ? "-" : "";
            var abs = Math.abs(value);
            var cur = symbol || iso || "";
            var p = (precision >= 0) ? precision : 2;
            var formatted = abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: p,
                maximumFractionDigits: p
            });
            return sign + (cur ? cur + " " : "") + formatted;
        }

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_InvoiceOverview.prototype.startPanel = function (windowNo, curTab) {
        this.windowNo = windowNo;
        this.curTab = curTab;
        if (curTab && typeof curTab.getAD_Table_ID === "function") {
            this.table_ID = curTab.getAD_Table_ID();
        }
        this.init();
    };

    /* Update tab panel based on selected record */
    VAS.VAS_InvoiceOverview.prototype.refreshPanelData = function (recordID, selectedRow) {
        if (selectedRow == undefined || recordID <= 0) {
            this.clear();
            return;
        }
        this.record_ID = recordID;
        this.selectedRow = selectedRow;
        this.fetchData(recordID);
    };

    /* Set width as per window width — default 25% on right pane */
    VAS.VAS_InvoiceOverview.prototype.sizeChanged = function (width) {
        this.panelWidth = width;
    };

    /* Release variables from memory */
    VAS.VAS_InvoiceOverview.prototype.dispose = function () {
        this.record_ID = 0;
        this.table_ID = 0;
        this.windowNo = 0;
        this.curTab = null;
        this.selectedRow = null;
        this.panelWidth = null;
    };

})(VAS, jQuery);
