#![deny(unsafe_op_in_unsafe_fn)]

use std::slice;

const VALID: i32 = 0;
const INVALID_HEADER: i32 = 1;
const WRONG_GAME: i32 = 2;
const UNSUPPORTED_REVISION: i32 = 3;

#[no_mangle]
pub unsafe extern "C" fn pipewizard_validate_disc_header(header: *const u8, length: usize) -> i32 {
    if header.is_null() || length < 8 {
        return INVALID_HEADER;
    }

    let bytes = unsafe { slice::from_raw_parts(header, length) };
    if &bytes[..6] != b"SMNE01" {
        return WRONG_GAME;
    }

    match bytes[7] {
        1 | 2 => VALID,
        _ => UNSUPPORTED_REVISION,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accepts_supported_disc_revisions() {
        for revision in [1, 2] {
            let header = [b'S', b'M', b'N', b'E', b'0', b'1', 0, revision];
            assert_eq!(
                unsafe { pipewizard_validate_disc_header(header.as_ptr(), header.len()) },
                VALID
            );
        }
    }

    #[test]
    fn rejects_wrong_game_and_unsupported_revisions() {
        let wrong_game = [b'S', b'M', b'N', b'E', b'0', b'2', 0, 1];
        let unsupported_revision = [b'S', b'M', b'N', b'E', b'0', b'1', 0, 3];
        assert_eq!(
            unsafe { pipewizard_validate_disc_header(wrong_game.as_ptr(), wrong_game.len()) },
            WRONG_GAME
        );
        assert_eq!(
            unsafe {
                pipewizard_validate_disc_header(
                    unsupported_revision.as_ptr(),
                    unsupported_revision.len()
                )
            },
            UNSUPPORTED_REVISION
        );
    }

    #[test]
    fn rejects_null_or_short_headers() {
        let short = [b'S', b'M', b'N', b'E', b'0', b'1', 0];
        assert_eq!(
            unsafe { pipewizard_validate_disc_header(std::ptr::null(), 8) },
            INVALID_HEADER
        );
        assert_eq!(
            unsafe { pipewizard_validate_disc_header(short.as_ptr(), short.len()) },
            INVALID_HEADER
        );
    }
}
