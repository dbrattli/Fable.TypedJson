// decision: skips initialization because successful record decoding overwrites every slot
export function createRecordBuffer(length) {
    return new Array(length);
}
