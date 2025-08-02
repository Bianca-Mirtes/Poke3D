# card_crop.py
import cv2
import numpy as np
import sys
import os

def find_card(image_path, save_path="cropped_card.png"):
    image = cv2.imread(image_path)
    if image is None:
        print("Imagem não encontrada:", image_path)
        return

    original = image.copy()
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    blur = cv2.GaussianBlur(gray, (5,5), 0)
    edged = cv2.Canny(blur, 50, 150)

    contours, _ = cv2.findContours(edged, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    contours = sorted(contours, key=cv2.contourArea, reverse=True)

    for c in contours:
        peri = cv2.arcLength(c, True)
        approx = cv2.approxPolyDP(c, 0.02 * peri, True)

        if len(approx) == 4:
            pts = approx.reshape(4, 2)

            rect = np.zeros((4, 2), dtype="float32")
            s = pts.sum(axis=1)
            rect[0] = pts[np.argmin(s)]
            rect[2] = pts[np.argmax(s)]

            diff = np.diff(pts, axis=1)
            rect[1] = pts[np.argmin(diff)]
            rect[3] = pts[np.argmax(diff)]

            (tl, tr, br, bl) = rect
            width = max(np.linalg.norm(br - bl), np.linalg.norm(tr - tl))
            height = max(np.linalg.norm(tr - br), np.linalg.norm(tl - bl))
            dst = np.array([[0, 0], [width, 0], [width, height], [0, height]], dtype="float32")

            M = cv2.getPerspectiveTransform(rect, dst)
            warp = cv2.warpPerspective(original, M, (int(width), int(height)))

            cv2.imwrite(save_path, warp)
            print("SUCCESS")
            return

    print("CARD_NOT_FOUND")

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("USAGE: python card_crop.py path_to_image")
        sys.exit(1)

    input_path = sys.argv[1]
    output_path = os.path.join(os.path.dirname(input_path), "cropped_card.png")
    find_card(input_path, output_path)
