// krzw(multi-edition): modal wrapper for adding an edition row to an existing movie
import React from 'react';
import Modal from 'Components/Modal/Modal';
import AddEditionModalContent, {
  AddEditionModalContentProps,
} from './AddEditionModalContent';

interface AddEditionModalProps extends AddEditionModalContentProps {
  isOpen: boolean;
}

function AddEditionModal({
  isOpen,
  onModalClose,
  ...otherProps
}: AddEditionModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <AddEditionModalContent {...otherProps} onModalClose={onModalClose} />
    </Modal>
  );
}

export default AddEditionModal;
