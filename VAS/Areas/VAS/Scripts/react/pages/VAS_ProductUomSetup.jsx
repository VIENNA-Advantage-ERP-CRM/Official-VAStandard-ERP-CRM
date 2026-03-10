import ProdUomComp from '../component/ProductUOMComponent';

const VAS_ProductUomSetup = ({ self }) => {
    return (
        <>
            <ProdUomComp windowNo={self.windowNo} frame={self.frame} />
        </>
    );
};
export default VAS_ProductUomSetup;