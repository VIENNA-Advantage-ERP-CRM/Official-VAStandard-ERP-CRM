/********************************************************
 * Module Name    : VAS_Standard
 * Purpose        : Reusable parametric form to send a document
 *                  through the VA112 PrintViewer share/email
 *                  panel. Resolves the recipient (Name + EMail)
 *                  on the server when not supplied, opens the
 *                  VA112.printViewer with dynamic parameters,
 *                  and walks Share -> Next -> fill recipient ->
 *                  Add. Built to support multiple concurrent
 *                  windowNo instances - every selector uses the
 *                  caller's windowNo.
 * Class Used     : VAS.VAS_SentEmailDoc
 * Chronological Development
 * Created Date   : 08-May-2026
 ********************************************************/

; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_SentEmailDoc = function () {
        this.frame = null;
        this.windowNo = null;

        var $self = this;
        var $root = null;
        var $bsyDiv = null;
        var log = VIS.Logging.VLogger.getVLogger("VAS_SentEmailDoc");

        /* Public API for callers that just want the parametric flow
         * without rendering a frame. Validates VA112 availability,
         * resolves the recipient on the server when needed, opens the
         * PrintViewer and drives the share panel. */
        function sendEmail(opts) {
            opts = opts || {};

            // 1. VA112 module must be installed.
            if (typeof window.VA112 === "undefined" || !window.VA112 || typeof VA112.printViewer !== "function") {
                VIS.ADialog.error("", "", VIS.Msg.getMsg("VAS_VA112ModuleNotInstalled"));
                return;
            }

            var windowNo = VIS.Utility.Util.getValueOfInt(opts.windowNo);
            var processId = VIS.Utility.Util.getValueOfInt(opts.AD_Process_ID);
            var tableId = VIS.Utility.Util.getValueOfInt(opts.AD_Table_ID);
            var recordId = VIS.Utility.Util.getValueOfInt(opts.RecordID);
            var adWindowId = VIS.Utility.Util.getValueOfInt(opts.AD_Window_ID);
            var recipientNm = (opts.Name == null) ? "" : ("" + opts.Name).trim();
            var recipientEm = (opts.EMailID == null) ? "" : ("" + opts.EMailID).trim();

            if (windowNo <= 0 || processId <= 0 || tableId <= 0 || recordId <= 0 || adWindowId <= 0) {
                VIS.ADialog.error("", "", VIS.Msg.getMsg("VAS_MissingParameters"));
                return;
            }

            // 2. Caller already provided both Name and EMail — skip the
            // server lookup entirely and go straight to the share panel.
            if (recipientNm && recipientEm) {
                openAndAutomate(windowNo, processId, tableId, recordId, adWindowId, recipientNm, recipientEm);
                return;
            }
            else {
                // 3. Resolve missing recipient details on the server using
                // a parameterized AD_Table_ID / RecordID -> C_BPartner_ID
                // -> AD_User chain.
                VIS.dataContext.getJSONData(
                    VIS.Application.contextUrl + "VAS_SentEmailDoc/GetRecipientInfo",
                    {
                        AD_Table_ID: tableId,
                        RecordID: recordId,
                        Name: recipientNm,
                        EMailID: recipientEm
                    },
                    function (raw) {
                        var res = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                        if (!res || !res.Success) {
                            var msg = (res && res.Message) ? res.Message : (VIS.Msg.getMsg("VAS_RecipientLookupFailed"));
                            VIS.ADialog.error("", "", msg);
                            return;
                        }

                        openAndAutomate(windowNo, processId, tableId, recordId, adWindowId, res.Name, res.EMailID);
                    },
                    function (err) {
                        log.severe("GetRecipientInfo failed: " + (err && err.statusText));
                        VIS.ADialog.error("", "", VIS.Msg.getMsg("VAS_RecipientLookupFailed"));
                    }
                );
            }
        }

        /* Open VA112.printViewer and drive the share/email panel:
         *   click .VA112-ShareDocDiv
         *   -> click .VA112-NextBtn.NextBtn[id^='NextBtn_']
         *   -> uncheck #authentication-required_<windowNo> (set to false)
         *   -> fill #VA112-SignerName_<windowNo>, #recipient-email_<windowNo>
         *   -> click #add-recipient-btn_<windowNo>
         * All selectors are scoped by windowNo so multiple instances
         * can run concurrently. */
        function openAndAutomate(windowNo, processId, tableId, recordId, adWindowId, toName, toEmail) {
            var pv;
            try {
                pv = new VA112.printViewer(windowNo, processId, tableId, recordId, "", adWindowId);
                pv.show();
            } catch (e) {
                log.severe("VA112.printViewer failed: " + (e && e.message));
                VIS.ADialog.error("", "", VIS.Msg.getMsg("VAS_PrintFailed"));
                return;
            }

            var nextSel = ".VA112-NextBtn.NextBtn[id^='NextBtn_'], [id^='NextBtn_'].VA112-NextBtn";
            var authSel = "#authentication-required_" + windowNo;
            var nameSel = "#VA112-SignerName_" + windowNo;
            var emailSel = "#recipient-email_" + windowNo;
            var addSel = "#add-recipient-btn_" + windowNo;

            clickUntilAppears(".VA112-ShareDocDiv", nextSel, function (next) {
                clickUntilAppears(next, nameSel, function () {
                    setAuthRequired(authSel, false, function () {
                        waitForEl(nameSel, function (nameEl) {
                            if (!nameEl) return;
                            setNativeInputValue(nameEl, toName || "");
                            waitForEl(emailSel, function (emailEl) {
                                if (!emailEl) return;
                                setNativeInputValue(emailEl, toEmail || "");
                                // Let the React state for both inputs flush, then
                                // wait until React has actually wired onClick on
                                // the + button before clicking it. Clicking
                                // before the prop is bound is silently dropped.
                                requestAnimationFrame(function () {
                                    waitForReactBinding(
                                        function () { return findInAllDocs(addSel); },
                                        "onClick",
                                        function (addBtn) {
                                            if (!addBtn) return;
                                            nativeClick(addBtn);
                                        }
                                    );
                                });
                            });
                        });
                    });
                });
            });
        }

        /* Set the authentication-required checkbox to `desired`.
         * Only fires a click when the current checked state differs
         * from the desired state, so a re-run does not toggle it
         * back. Falls through cleanly if the element never appears. */
        function setAuthRequired(selector, desired, callback) {
            waitForEl(selector, function (el) {
                if (!el) { callback(); return; }
                var current = !!el.checked;
                if (current === desired) { callback(); return; }
                nativeClick(el);
                // Confirm the state flipped; if the click landed before
                // React bound onChange (cold open), retry once after a
                // frame. Either way we move on so the chain never wedges.
                requestAnimationFrame(function () {
                    if (!!el.checked !== desired) {
                        try {
                            var proto = window.HTMLInputElement.prototype;
                            var setter = Object.getOwnPropertyDescriptor(proto, "checked");
                            if (setter && setter.set) { setter.set.call(el, desired); }
                            el.dispatchEvent(new Event("click", { bubbles: true }));
                            el.dispatchEvent(new Event("change", { bubbles: true }));
                        } catch (e) { /* ignore */ }
                    }
                    callback();
                });
            });
        }

        /* Click `clickTarget` (selector or DOM node) and wait for
         * `successSelector` to appear. If it does not appear within
         * `windowMs`, click again - up to `maxClicks` retries. */
        function clickUntilAppears(clickTarget, successSelector, callback, maxClicks, windowMs) {
            var max = maxClicks || 6;
            var win = windowMs || 700;
            var clicks = 0;

            function resolveTarget(cb) {
                if (typeof clickTarget !== "string") { cb(clickTarget); return; }
                waitForEl(clickTarget, cb);
            }

            function tryOnce(target) {
                if (!target) return;
                requestAnimationFrame(function () {
                    requestAnimationFrame(function () {
                        nativeClick(target);
                        clicks++;
                        var deadline = Date.now() + win;
                        var timer = setInterval(function () {
                            var found = findInAllDocs(successSelector);
                            if (found) {
                                clearInterval(timer);
                                callback(found);
                            } else if (Date.now() > deadline) {
                                clearInterval(timer);
                                if (clicks < max) {
                                    tryOnce(target);
                                } else {
                                    waitForEl(successSelector, callback);
                                }
                            }
                        }, 60);
                    });
                });
            }

            resolveTarget(tryOnce);
        }

        /* Fire a click that survives both React's synthetic-event
         * system and plain-DOM listeners. Reads onClick straight off
         * the React Fiber when present, otherwise dispatches a full
         * pointer + mouse sequence using the element's own window. */
        function nativeClick(el) {
            if (!el) return;

            var keys = Object.keys(el);
            var propsKey = null;
            for (var i = 0; i < keys.length; i++) {
                if (keys[i].indexOf("__reactProps$") === 0
                    || keys[i].indexOf("__reactEventHandlers$") === 0) {
                    propsKey = keys[i];
                    break;
                }
            }
            var reactProps = propsKey ? el[propsKey] : null;
            if (reactProps && typeof reactProps.onClick === "function") {
                try {
                    reactProps.onClick({
                        preventDefault: function () { },
                        stopPropagation: function () { },
                        persist: function () { },
                        isDefaultPrevented: function () { return false; },
                        isPropagationStopped: function () { return false; },
                        nativeEvent: {},
                        target: el,
                        currentTarget: el,
                        type: "click",
                        bubbles: true,
                        cancelable: true,
                        button: 0
                    });
                    return;
                } catch (e) { /* fall through */ }
            }

            var win = el.ownerDocument.defaultView || window;
            var WinMouseEvent = win.MouseEvent || MouseEvent;
            var WinPointerEvent = win.PointerEvent;
            var opts = { bubbles: true, cancelable: true, view: win, button: 0 };
            try {
                if (typeof WinPointerEvent === "function") {
                    el.dispatchEvent(new WinPointerEvent("pointerdown", opts));
                }
                el.dispatchEvent(new WinMouseEvent("mousedown", opts));
                if (typeof WinPointerEvent === "function") {
                    el.dispatchEvent(new WinPointerEvent("pointerup", opts));
                }
                el.dispatchEvent(new WinMouseEvent("mouseup", opts));
                el.dispatchEvent(new WinMouseEvent("click", opts));
            } catch (e) {
                if (typeof el.click === "function") { el.click(); }
            }
        }

        /* Poll per-frame until React has bound `propName` on the
         * element returned by getEl(). Resolves the element each
         * tick so a remount during the wait does not leave us
         * holding a stale reference. */
        function waitForReactBinding(getEl, propName, callback, timeoutMs) {
            var deadline = Date.now() + (timeoutMs || 15000);
            (function check() {
                var el = (typeof getEl === "function") ? getEl() : getEl;
                if (el) {
                    var keys = Object.keys(el);
                    for (var i = 0; i < keys.length; i++) {
                        var k = keys[i];
                        if (k.indexOf("__reactProps$") === 0
                            || k.indexOf("__reactEventHandlers$") === 0) {
                            if (el[k] && typeof el[k][propName] === "function") {
                                callback(el);
                                return;
                            }
                            break;
                        }
                    }
                }
                if (Date.now() > deadline) { callback(el || null); return; }
                requestAnimationFrame(check);
            })();
        }

        /* Use the native HTMLInputElement value setter so React's
         * controlled-input wrapper picks up the change. */
        function setNativeInputValue(el, value) {
            if (!el) return;
            var proto = (el.tagName === "TEXTAREA")
                ? window.HTMLTextAreaElement.prototype
                : window.HTMLInputElement.prototype;
            var setter = Object.getOwnPropertyDescriptor(proto, "value");
            if (setter && setter.set) {
                setter.set.call(el, value);
            } else {
                el.value = value;
            }
            el.dispatchEvent(new Event("input", { bubbles: true }));
            el.dispatchEvent(new Event("change", { bubbles: true }));
        }

        /* Wait for an element to appear, including in same-origin
         * iframes (the PrintViewer renders inside one). Returns the
         * raw DOM node so callers can dispatch native events. */
        function waitForEl(selector, callback, timeoutMs) {
            var timeout = timeoutMs || 15000;
            var settled = false;
            var observers = [];
            var pollTimer = null;
            var deadline = null;

            function settle(node) {
                if (settled) return;
                settled = true;
                for (var i = 0; i < observers.length; i++) {
                    try { observers[i].disconnect(); } catch (e) { }
                }
                if (pollTimer) { clearInterval(pollTimer); }
                if (deadline) { clearTimeout(deadline); }
                callback(node);
            }

            var found = findInAllDocs(selector);
            if (found) { settle(found); return; }

            function observe(doc) {
                if (!doc || !doc.body) return;
                var obs = new MutationObserver(function () {
                    var hit = findInAllDocs(selector);
                    if (hit) { settle(hit); }
                });
                obs.observe(doc.body, { childList: true, subtree: true });
                observers.push(obs);
            }
            observe(document);
            var frames = document.getElementsByTagName("iframe");
            for (var i = 0; i < frames.length; i++) {
                try { observe(frames[i].contentDocument); } catch (e) { }
            }

            pollTimer = setInterval(function () {
                var hit = findInAllDocs(selector);
                if (hit) { settle(hit); return; }
                var fr = document.getElementsByTagName("iframe");
                for (var k = 0; k < fr.length; k++) {
                    try {
                        var d = fr[k].contentDocument;
                        if (d && observers.indexOf(d) === -1) { observe(d); }
                    } catch (e) { }
                }
            }, 250);

            deadline = setTimeout(function () {
                if (!settled) {
                    settled = true;
                    for (var j = 0; j < observers.length; j++) {
                        try { observers[j].disconnect(); } catch (e) { }
                    }
                    if (pollTimer) { clearInterval(pollTimer); }
                    callback(null);
                }
            }, timeout);
        }

        function findInAllDocs(selector) {
            var docs = [document];
            var frames = document.getElementsByTagName("iframe");
            for (var i = 0; i < frames.length; i++) {
                try {
                    var d = frames[i].contentDocument;
                    if (d) { docs.push(d); }
                } catch (e) { }
            }
            for (var j = 0; j < docs.length; j++) {
                var nodes = docs[j].querySelectorAll(selector);
                for (var k = 0; k < nodes.length; k++) {
                    var n = nodes[k];
                    if (n.offsetParent !== null || n.getClientRects().length > 0) {
                        return n;
                    }
                }
                if (nodes.length) { return nodes[0]; }
            }
            return null;
        }

        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap" style="visibility:hidden"><div class="vis-busyindicatorinnerwrap"><i class="vis-busyindicatordiv"></i></div></div>');
            $root.append($bsyDiv);
        }

        // Expose the parametric entry point on instances and as a
        // static function so callers can use either flavour.
        this.sendEmail = sendEmail;

        this.Initialize = function () {
            $root = $('<div class="VAS-SentEmailDoc-root" style="display:none"></div>');
            createBusyIndicator();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            if ($root) { $root.remove(); }
            $root = null;
            $bsyDiv = null;
            this.getRoot = null;
            this.disposeComponent = null;
        };
    };

    /* Static entry point so callers do not need to instantiate a
     * frame just to fire the share/email flow:
     *   VAS.VAS_SentEmailDoc.sendEmail({ windowNo, AD_Process_ID,
     *       AD_Table_ID, RecordID, AD_Window_ID, Name, EMailID });
     */
    VAS.VAS_SentEmailDoc.sendEmail = function (opts) {
        var inst = new VAS.VAS_SentEmailDoc();
        inst.sendEmail(opts);
    };

    // Standard frame lifecycle so the form can also be opened
    // through the framework menu when needed. In that case the
    // caller is expected to invoke sendEmail() through the
    // returned instance.
    VAS.VAS_SentEmailDoc.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        if (frame && typeof frame.hideHeader === "function") {
            frame.hideHeader(true);
        }
        this.Initialize();
        if (frame && typeof frame.getContentGrid === "function") {
            frame.getContentGrid().append(this.getRoot());
        }
    };

    VAS.VAS_SentEmailDoc.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame && typeof this.frame.dispose === "function") {
            this.frame.dispose();
        }
        this.frame = null;
    };

    VAS.VAS_SentEmailDoc.prototype.setWidth = function () { return 480; };
    VAS.VAS_SentEmailDoc.prototype.setHeight = function () { return 280; };

})(VAS, jQuery);
