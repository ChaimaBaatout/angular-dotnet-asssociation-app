import { Component, ElementRef, inject, OnDestroy, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Router, RouterLink } from '@angular/router';
import jsQR from 'jsqr';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/errors';
import { tokenFromQrText } from '../../core/link-token';

const NOT_A_SIGN_IN_CODE = 'This is not an Admin Portal sign-in code.';

/** Signs in by reading a QR code with this device's camera, or from a saved picture of it. */
@Component({
  selector: 'app-qr-scan',
  imports: [RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './qr-scan.html',
  styles: `
    video {
      display: block;
      width: 100%;
      margin-bottom: 16px;
      border-radius: 12px;
      background: #000;

      &[hidden] {
        display: none;
      }
    }

    .options {
      display: grid;
      gap: 12px;
    }
  `,
})
export class QrScan implements OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly video = viewChild.required<ElementRef<HTMLVideoElement>>('video');
  private readonly canvas = document.createElement('canvas');

  private stream: MediaStream | null = null;
  private frameRequest = 0;

  protected readonly scanning = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnDestroy(): void {
    this.stopCamera();
  }

  protected async startCamera(): Promise<void> {
    this.error.set(null);
    if (!navigator.mediaDevices?.getUserMedia) {
      this.error.set('This browser cannot use the camera on this connection. Choose a picture of the code instead.');
      return;
    }
    try {
      this.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
      const video = this.video().nativeElement;
      video.srcObject = this.stream;
      await video.play();
      this.scanning.set(true);
      this.frameRequest = requestAnimationFrame(() => this.scanFrame());
    } catch {
      this.stopCamera();
      this.error.set('The camera is not available. Allow access to it, or choose a picture of the code instead.');
    }
  }

  protected stopCamera(): void {
    cancelAnimationFrame(this.frameRequest);
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = null;
    this.scanning.set(false);
  }

  protected async readFile(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.stopCamera();
    this.error.set(null);
    try {
      const image = await createImageBitmap(file);
      const text = this.decode(image, image.width, image.height);
      if (text) this.signIn(text);
      else this.error.set('No QR code was found in this picture.');
    } catch {
      this.error.set('This file could not be read as a picture.');
    }
  }

  private scanFrame(): void {
    const video = this.video().nativeElement;
    if (!this.stream) return;

    const text = video.videoWidth ? this.decode(video, video.videoWidth, video.videoHeight) : null;
    if (text && tokenFromQrText(text)) {
      this.stopCamera();
      this.signIn(text);
      return;
    }
    if (text) this.error.set(NOT_A_SIGN_IN_CODE);
    this.frameRequest = requestAnimationFrame(() => this.scanFrame());
  }

  private decode(source: CanvasImageSource, width: number, height: number): string | null {
    this.canvas.width = width;
    this.canvas.height = height;
    const context = this.canvas.getContext('2d', { willReadFrequently: true })!;
    context.drawImage(source, 0, 0, width, height);
    const { data } = context.getImageData(0, 0, width, height);
    return jsQR(data, width, height)?.data ?? null;
  }

  private signIn(text: string): void {
    const token = tokenFromQrText(text);
    if (!token) {
      this.error.set(NOT_A_SIGN_IN_CODE);
      return;
    }
    this.busy.set(true);
    this.auth.qrLogin(token).subscribe({
      next: () => void this.router.navigateByUrl('/'),
      error: (error) => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      },
    });
  }
}
