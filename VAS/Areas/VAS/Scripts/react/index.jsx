import React from 'react';
import ReactDOM from 'react-dom/client';

//VIS Replace with module prefix

// Ensure VIS namespace and dependencies are available
window.VIS = window.VIS || {};
(function (VIS, $) {
    // Ensure VIS.React namespace is initialized
    VIS.React = VIS.React || {};

    // Constructor function for VIS.React
    VIS.React = function () {
        this.listener=null;
    };

    // Initialize method to render React component dynamically
    VIS.React.prototype.init = function (windowNo, frame, additionalInfo) {
        this.windowNo = windowNo;
        this.frame = frame;
        var componentName = frame.componentName;
        let self = this;
       
        // Lazy load the component based on componentName
        const MyComponent = React.lazy(() => import(`./pages/${componentName}`));

        // Render the component with Suspense for fallback
        ReactDOM.createRoot(frame.getContentGrid()[0]).render(
            <React.Suspense fallback={<div>Loading...</div>}>
                <MyComponent self={self} />
            </React.Suspense>
        );
    };

    VIS.React.prototype.widgetFirevalueChanged = function (value) {
        // this.getRoot().trigger('widgetFirevalueChanged', value); // Trigger custom event with the value
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    VIS.React.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    }

})(VIS, jQuery);

