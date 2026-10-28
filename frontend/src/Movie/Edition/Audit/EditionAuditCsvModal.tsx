// krzw(edition-runtime-catalog): CSV of the visible audit rows in a read-only textarea with a copy button (no blob downloads)
import React, { useCallback } from 'react';
import TextArea from 'Components/Form/TextArea';
import Button from 'Components/Link/Button';
import ClipboardButton from 'Components/Link/ClipboardButton';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './EditionAudit.css';

interface EditionAuditCsvModalProps {
  isOpen: boolean;
  csv: string;
  onModalClose: () => void;
}

function EditionAuditCsvModal({
  isOpen,
  csv,
  onModalClose,
}: EditionAuditCsvModalProps) {
  const handleChange = useCallback(() => {
    // read-only
  }, []);

  return (
    <Modal isOpen={isOpen} size={sizes.LARGE} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>{translate('EditionAuditCsvTitle')}</ModalHeader>

        <ModalBody>
          <TextArea
            className={styles.csv}
            name="csv"
            value={csv}
            readOnly={true}
            onChange={handleChange}
          />
        </ModalBody>

        <ModalFooter>
          <ClipboardButton value={csv} label={translate('CopyToClipboard')} />

          <Button onPress={onModalClose}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default EditionAuditCsvModal;
