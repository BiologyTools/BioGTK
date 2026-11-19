"""Export a micro-sam2 checkpoint for BioGTK's ONNX image segmentation tool."""

import argparse
from pathlib import Path
from tempfile import TemporaryDirectory

import torch


FEATURES = ("image_embed", "high_res_feats_0", "high_res_feats_1")
DECODER_INPUTS = (*FEATURES, "point_coords", "point_labels")
DECODER_OUTPUTS = ("mask_logits", "iou_predictions")


class ImageEncoder(torch.nn.Module):
    def __init__(self, model):
        super().__init__()
        self.model = model

    def forward(self, image):
        backbone = self.model.forward_image(image)
        _, features, _, _ = self.model._prepare_backbone_features(backbone)
        high_res_0, high_res_1, embedding = features
        if self.model.directly_add_no_mem_embed:
            embedding = embedding + self.model.no_mem_embed
        return (
            embedding.permute(1, 2, 0).reshape(1, 256, 64, 64),
            high_res_0.permute(1, 2, 0).reshape(1, 32, 256, 256),
            high_res_1.permute(1, 2, 0).reshape(1, 64, 128, 128),
        )


class PromptDecoder(torch.nn.Module):
    def __init__(self, model):
        super().__init__()
        self.prompt_encoder = model.sam_prompt_encoder
        self.mask_decoder = model.sam_mask_decoder
        self.register_buffer("image_pe", self.prompt_encoder.get_dense_pe())

    def forward(self, image_embed, high_res_feats_0, high_res_feats_1, point_coords, point_labels):
        sparse, dense = self.prompt_encoder(points=(point_coords, point_labels), boxes=None, masks=None)
        masks, iou, _, _ = self.mask_decoder(
            image_embeddings=image_embed,
            image_pe=self.image_pe,
            sparse_prompt_embeddings=sparse,
            dense_prompt_embeddings=dense,
            multimask_output=False,
            repeat_image=False,
            high_res_features=[high_res_feats_0, high_res_feats_1],
        )
        return masks.reshape(1, 1, 256, 256), iou.reshape(1, 1)


def verify_exports(encoder_path, decoder_path, encoder, decoder, image):
    import numpy as np
    import onnxruntime as ort

    options = ort.SessionOptions()
    options.intra_op_num_threads = 4
    providers = ["CPUExecutionProvider"]
    encoder_session = ort.InferenceSession(str(encoder_path), options, providers=providers)
    decoder_session = ort.InferenceSession(str(decoder_path), options, providers=providers)
    expected_features = encoder(image)
    features = encoder_session.run(list(FEATURES), {"image": image.numpy()})
    for actual, expected in zip(features, expected_features):
        np.testing.assert_allclose(actual, expected.numpy(), atol=5e-3, rtol=5e-3)

    # Check a point and a box with a corrective point. The graph must accept different prompt counts.
    for coords, labels in (
        ([[304, 312]], [1]),
        ([[180, 160], [420, 480], [220, 200]], [2, 3, 0]),
    ):
        points = torch.tensor([coords], dtype=torch.float32)
        types = torch.tensor([labels], dtype=torch.float32)
        expected_outputs = decoder(*expected_features, points, types)
        inputs = dict(zip(DECODER_INPUTS, (*features, points.numpy(), types.numpy())))
        outputs = decoder_session.run(list(DECODER_OUTPUTS), inputs)
        for actual, expected in zip(outputs, expected_outputs):
            np.testing.assert_allclose(actual, expected.numpy(), atol=5e-2, rtol=1e-2)


@torch.inference_mode()
def export(output_dir, model_type):
    from micro_sam.v2.util import get_sam2_model, models

    torch.set_num_threads(4)
    torch.manual_seed(0)
    checkpoint = models().fetch(model_type, progressbar=True)
    model = get_sam2_model(model_type=model_type, checkpoint_path=checkpoint, device="cpu").eval()
    encoder = ImageEncoder(model).eval()
    decoder = PromptDecoder(model).eval()
    image = torch.rand(1, 3, 1024, 1024)
    points = torch.tensor([[[304.0, 312.0]]])
    labels = torch.tensor([[1.0]])

    output_dir.mkdir(parents=True, exist_ok=True)
    with TemporaryDirectory(dir=output_dir) as stage:
        encoder_path = Path(stage) / "micro-sam2-encoder.onnx"
        decoder_path = Path(stage) / "micro-sam2-decoder.onnx"
        torch.onnx.export(
            encoder, (image,), str(encoder_path),
            input_names=["image"], output_names=list(FEATURES),
            opset_version=17, dynamo=False,
        )
        torch.onnx.export(
            decoder, (*encoder(image), points, labels), str(decoder_path),
            input_names=list(DECODER_INPUTS), output_names=list(DECODER_OUTPUTS),
            dynamic_axes={"point_coords": {1: "num_points"}, "point_labels": {1: "num_points"}},
            opset_version=17, dynamo=False,
        )
        verify_exports(encoder_path, decoder_path, encoder, decoder, image)
        encoder_path.replace(output_dir / encoder_path.name)
        decoder_path.replace(output_dir / decoder_path.name)
    print(f"Verified and exported the {model_type} model pair to {output_dir.resolve()}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output_dir", type=Path)
    parser.add_argument(
        "--model", default="hvit_t_cells",
        choices=("hvit_t_cells", "hvit_s_cells", "hvit_b_cells", "hvit_l_cells", "hvit_s_em_mito", "hvit_s_em_nuclei"),
    )
    args = parser.parse_args()
    export(args.output_dir, args.model)
